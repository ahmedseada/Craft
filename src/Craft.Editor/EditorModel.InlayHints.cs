// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// Inlay hints (X part 9): the lines on screen (and a screen either side) are asked of the language's semantic layer
/// (<see cref="ISemanticLanguageSession.InlayHintsAsync"/>) after <c>CRAFT_INLAY_HINTS_DELAY_MS</c> without a change or a
/// scroll, off the UI thread; the answer is kept for its version and range (a frame over the same lines asks nothing and
/// allocates nothing), moved through each edit (a hint the edit touched is dropped) until the next answer replaces it.
/// They are drawn as faded chips inside the text (<see cref="EditorRenderer"/>): the caret, the selection and the columns
/// never count them; the text after one is only drawn further right. <c>CRAFT_INLAY_HINTS</c>: on, off, or ctrl-held
/// (<see cref="ControlHeld"/>, told by the host).
/// </summary>
public sealed partial class EditorModel
{
    private InlayHint[] _hints = [];
    private int _hintsVersion = -1;
    private int _hintsFrom;
    private int _hintsTo;
    private bool _hintsStale;
    private int _hintsAskedVersion = -1;
    private int _hintsAskedFrom;
    private int _hintsAskedTo;
    private DebounceTimer? _hintsTimer;
    private CancellationTokenSource? _hintsRequest;
    private Action? _hintsDue;
    private bool _controlHeld;

    /// <summary>
    /// Whether Ctrl is held (the host tells it as the key goes down and up): with <c>CRAFT_INLAY_HINTS</c> "ctrl-held" the
    /// hints show only meanwhile.
    /// </summary>
    public bool ControlHeld
    {
        get => _controlHeld;
        set
        {
            if (_controlHeld != value)
            {
                _controlHeld = value;
                if (Options.InlayHints == "ctrl-held")
                {
                    Invalidate();
                }
            }
        }
    }

    /// <summary>Whether inlay hints are drawn now (the setting, Ctrl for "ctrl-held", and a semantic layer to ask).</summary>
    public bool InlayHintsShown => _session is ISemanticLanguageSession && Options.InlayHints switch
    {
        "on" => true,
        "ctrl-held" => _controlHeld,
        _ => false,
    };

    /// <summary>The hints kept now, in order of position, in the current text's positions.</summary>
    public IReadOnlyList<InlayHint> InlayHints => _hints;

    /// <summary>The requests made for inlay hints since the editor opened.</summary>
    public int InlayHintRequests { get; private set; }

    /// <summary>The range of the current text the kept hints cover (from, to), or null when none were asked yet.</summary>
    public (int From, int To)? InlayHintsRange => _hintsVersion < 0 ? null : (_hintsFrom, _hintsTo);

    /// <summary>The kept hints from <paramref name="start"/> to <paramref name="end"/> (both included), a slice of the kept array: no copy.</summary>
    internal ReadOnlySpan<InlayHint> InlayHintsBetween(int start, int end)
    {
        var hints = _hints;
        if (hints.Length == 0 || !InlayHintsShown)
        {
            return [];
        }

        int low = 0;
        int high = hints.Length;
        while (low < high)
        {
            int middle = (low + high) >>> 1;
            if (hints[middle].Position < start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        int last = low;
        while (last < hints.Length && hints[last].Position <= end)
        {
            last++;
        }

        return hints.AsSpan(low, last - low);
    }

    /// <summary>
    /// The renderer's frame shows <paramref name="start"/> to <paramref name="end"/>: when the kept hints do not cover them
    /// for this version, a request is scheduled after the pause (once per version and range; nothing allocated otherwise).
    /// </summary>
    internal void NoteHintsShown(int start, int end, int linesShown)
    {
        if (!InlayHintsShown || SemanticSession is not { } || _disposed)
        {
            return;
        }

        var snapshot = Snapshot;
        bool covered = _hintsVersion >= 0 && !_hintsStale && _hintsFrom <= start && _hintsTo >= end;
        if (covered)
        {
            return;
        }

        bool asked = _hintsAskedVersion == snapshot.Version && _hintsAskedFrom <= start && _hintsAskedTo >= end;
        if (asked)
        {
            return;
        }

        // A screen either side, so a short scroll finds them kept.
        int firstLine = snapshot.GetLineIndex(start);
        int lastLine = snapshot.GetLineIndex(end);
        int margin = Math.Max(1, linesShown);
        _hintsAskedVersion = snapshot.Version;
        _hintsAskedFrom = snapshot.GetLine(Math.Max(0, firstLine - margin)).Start;
        _hintsAskedTo = snapshot.GetLine(Math.Min(snapshot.LineCount - 1, lastLine + margin)).EndIncludingBreak;
        _hintsTimer ??= new DebounceTimer(this);
        _hintsTimer.Schedule(Options.InlayHintsDelayMilliseconds, _hintsDue ??= RequestHints);
    }

    private void RequestHints()
    {
        if (SemanticSession is not { } session || _disposed)
        {
            return;
        }

        var snapshot = Snapshot;
        if (_hintsAskedVersion != snapshot.Version)
        {
            return;                                           // a change since: the next frame asks for the new version
        }

        _hintsRequest?.Cancel();
        var cancel = new CancellationTokenSource();
        _hintsRequest = cancel;
        int from = Math.Clamp(_hintsAskedFrom, 0, snapshot.Length);
        int to = Math.Clamp(_hintsAskedTo, from, snapshot.Length);
        InlayHintRequests++;
        bool parameters = Options.InlayHintsParameters;
        bool types = Options.InlayHintsTypes;
        Track(session.InlayHintsAsync(snapshot, from, to - from, cancel.Token), hints =>
        {
            if (!ReferenceEquals(_hintsRequest, cancel))
            {
                return;
            }

            _hintsRequest = null;
            cancel.Dispose();
            if (Snapshot.Version != snapshot.Version)
            {
                return;                                       // stale: the hints moved through the edits stay until the next answer
            }

            var kept = new List<InlayHint>(hints.Count);
            foreach (var hint in hints)
            {
                bool wanted = hint.Kind switch
                {
                    InlayHintKind.Parameter => parameters,
                    InlayHintKind.Type => types,
                    _ => true,
                };
                if (wanted && hint.Position >= from && hint.Position <= to && hint.Label.Length > 0)
                {
                    kept.Add(hint);
                }
            }

            kept.Sort(static (a, b) => a.Position.CompareTo(b.Position));
            _hints = [.. kept];
            _hintsVersion = snapshot.Version;
            _hintsFrom = from;
            _hintsTo = to;
            _hintsStale = false;
            Invalidate();
        }, () =>
        {
            if (ReferenceEquals(_hintsRequest, cancel))
            {
                _hintsRequest = null;                          // failed: not asked again for this text (an edit asks again)
            }
        });
    }

    // Each change moves the kept hints (one the change touched is dropped); a request is due for the new text.
    private void MapHints(TextChangedEventArgs change)
    {
        if (_hints.Length == 0 && _hintsVersion < 0)
        {
            return;
        }

        var edits = change.Edits;
        var kept = new List<InlayHint>(_hints.Length);
        foreach (var hint in _hints)
        {
            int position = hint.Position;
            bool touched = false;
            int delta = 0;
            foreach (var edit in edits)
            {
                if (position > edit.End || (position == edit.End && edit.Length > 0))
                {
                    delta += edit.Text.Length - edit.Length;
                }
                else if (position >= edit.Start)
                {
                    touched = true;
                    break;
                }
                else
                {
                    break;
                }
            }

            if (!touched)
            {
                kept.Add(delta == 0 ? hint : hint with { Position = position + delta });
            }
        }

        _hints = [.. kept];
        _hintsFrom = MapThroughEdits(_hintsFrom, edits);
        _hintsTo = MapThroughEdits(_hintsTo, edits);
        _hintsVersion = change.After.Version;
        _hintsStale = true;
    }

    // The visual column of `row` a click at drawn column `drawn` means, past the inlay hints drawn before it there.
    private float HintedColumn(int row, float drawn)
    {
        var snapshot = Snapshot;
        int index = LineAtRow(Math.Clamp(row, 0, Math.Max(0, RowCount - 1)));
        var line = snapshot.GetLine(index);
        var hints = InlayHintsBetween(line.Start, line.End);
        if (hints.IsEmpty)
        {
            return drawn;
        }

        char[]? buffer = null;
        try
        {
            var text = TextReading.ReadLine(snapshot, line, ref buffer);
            var wrapped = WordWrap.IsActive ? WordWrap.Line(index, text) : null;
            int sub = wrapped is { Rows: > 1 } ? Math.Clamp(row - RowOfLine(index), 0, wrapped.Rows - 1) : 0;
            return new HintColumns(hints, text, line.Start, Options.TabSize, wrapped).FromDrawn((int)MathF.Round(drawn), sub);
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    private void DisposeHints()
    {
        _hintsTimer?.Dispose();
        _hintsTimer = null;
        _hintsRequest?.Cancel();
        _hintsRequest = null;
    }
}

/// <summary>
/// The columns of one drawn line with its inlay hints (X part 9): a character's or a caret's visual column moved right by
/// the cells of the hints before it on its row, so the renderer draws the text, the selection, the carets and the marks
/// after a hint further right while the editor's own columns stay the text's. A hint is drawn before the character at its
/// position; a caret at that position is drawn before the hint. On a wrapped line (main's word wrap) each row is its own:
/// a hint moves only what follows it on its row, in that row's visual columns (the wrap itself is unchanged).
/// </summary>
internal readonly ref struct HintColumns
{
    private readonly ReadOnlySpan<InlayHint> _hints;
    private readonly ReadOnlySpan<char> _text;
    private readonly WrappedLine? _wrapped;
    private readonly int _lineStart;
    private readonly int _tabSize;

    public HintColumns(ReadOnlySpan<InlayHint> hints, ReadOnlySpan<char> text, int lineStart, int tabSize, WrappedLine? wrapped = null)
    {
        _hints = hints;
        _text = text;
        _lineStart = lineStart;
        _tabSize = tabSize;
        _wrapped = wrapped is { Rows: > 1 } ? wrapped : null;
    }

    /// <summary>Whether the line has hints.</summary>
    public bool Any => !_hints.IsEmpty;

    /// <summary>The hints of the line.</summary>
    public ReadOnlySpan<InlayHint> Hints => _hints;

    private int IndexOf(int i) => Math.Clamp(_hints[i].Position - _lineStart, 0, _text.Length);

    /// <summary>The row (of a wrapped line; 0 otherwise) hint <paramref name="i"/> stands on.</summary>
    public int RowOf(int i) => _wrapped is { } wrapped ? wrapped.RowOf(IndexOf(i)) : 0;

    /// <summary>The visual column on its row of hint <paramref name="i"/> (its text column on a line that does not wrap).</summary>
    public int ColumnOf(int i)
    {
        int column = TextReading.ColumnOf(_text, IndexOf(i), _tabSize);
        return _wrapped is { } wrapped ? wrapped.VisualColumn(wrapped.RowOf(IndexOf(i)), column) : column;
    }

    /// <summary>The cells the hints of <paramref name="row"/> take.</summary>
    public int CellsOn(int row)
    {
        int cells = 0;
        for (int i = 0; i < _hints.Length; i++)
        {
            if (RowOf(i) == row)
            {
                cells += _hints[i].Cells;
            }
        }

        return cells;
    }

    /// <summary>Where the character at visual column <paramref name="column"/> of <paramref name="row"/> is drawn.</summary>
    public int Char(int column, int row = 0) => column + Shift(column, row, inclusive: true);

    /// <summary>Where a caret at visual column <paramref name="column"/> of <paramref name="row"/> is drawn (before a hint at its own column).</summary>
    public int Caret(int column, int row = 0) => column + Shift(column, row, inclusive: false);

    private int Shift(int column, int row, bool inclusive)
    {
        int shift = 0;
        for (int i = 0; i < _hints.Length; i++)
        {
            if (RowOf(i) != row)
            {
                continue;
            }

            int at = ColumnOf(i);
            if (inclusive ? at > column : at >= column)
            {
                break;
            }

            shift += _hints[i].Cells;
        }

        return shift;
    }

    /// <summary>The visual column of <paramref name="row"/> drawn at <paramref name="drawn"/> (a click's), the hint's own place when it falls on one.</summary>
    public int FromDrawn(int drawn, int row = 0)
    {
        int shift = 0;
        for (int i = 0; i < _hints.Length; i++)
        {
            if (RowOf(i) != row)
            {
                continue;
            }

            int column = ColumnOf(i);
            int start = column + shift;
            if (drawn < start)
            {
                break;
            }

            int cells = _hints[i].Cells;
            if (drawn < start + cells)
            {
                return column;                                 // on the chip: the place it stands at
            }

            shift += cells;
        }

        return Math.Max(0, drawn - shift);
    }
}
