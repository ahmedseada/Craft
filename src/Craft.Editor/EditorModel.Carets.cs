// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Runtime.InteropServices;
using Craft.Abstractions;

namespace Craft.Editor;

// Phase X: several carets. The primary selection is `_selection` (the one the view follows, the one a single-caret
// command works on); with more than one, `_carets` holds them all in order of position, merged where they overlap, and
// `_primary` indexes the primary. Typing, deleting, Enter, Tab, paste and the line commands make one edit per caret,
// all applied as one change (one undo step that restores every caret: IMultiSelectionDocument). The per-caret work is a
// loop over the carets into pooled buffers: nothing is allocated per keystroke but the change itself.
public sealed partial class EditorModel
{
    private readonly List<SelectionState> _carets = [];
    private int _primary;
    private int[] _desiredColumns = [];
    private int _desiredCount;
    private bool _occurrenceWhole;
    private bool _boxActive;
    private int _boxAnchorLine;
    private int _boxAnchorColumn;
    private int _boxLine;
    private int _boxColumn;
    private PointerMode _pointerMode;

    private enum PointerMode
    {
        Select,
        AddSelection,
        Box,
        Card,
        Ruler,
    }

    /// <summary>How many selections (carets) there are: 1, or more after Alt+click, Ctrl+D, select all occurrences or a column selection.</summary>
    public int SelectionCount => _carets.Count == 0 ? 1 : _carets.Count;

    /// <summary>Whether there is more than one caret.</summary>
    public bool HasMultipleCarets => _carets.Count > 1;

    /// <summary>Changes whenever the selections are set (the overview ruler rebuilds its caret marks on it).</summary>
    internal int SelectionStamp { get; private set; }

    /// <summary>Every selection, in order of position (one: <see cref="Selection"/>).</summary>
    public ReadOnlySpan<SelectionState> Selections => _carets.Count == 0 ? new ReadOnlySpan<SelectionState>(in _selection) : CollectionsMarshal.AsSpan(_carets);

    /// <summary>The index in <see cref="Selections"/> of the primary selection (<see cref="Selection"/>).</summary>
    public int PrimarySelectionIndex => _carets.Count == 0 ? 0 : _primary;

    /// <summary>What the last caret command refused (the caret limit reached), or null.</summary>
    public string? LastCaretMessage { get; private set; }

    /// <summary>
    /// Sets every selection (clamped to the text, sorted, merged where they overlap); <paramref name="primary"/> indexes the
    /// one the view follows. One selection is the same as <see cref="Select"/>.
    /// </summary>
    public void SetSelections(ReadOnlySpan<SelectionState> selections, int primary)
    {
        if (selections.IsEmpty)
        {
            return;
        }

        SetSelectionsCore(selections, Math.Clamp(primary, 0, selections.Length - 1), keepDesiredColumns: false, keepAutoClosed: false, keepBox: false);
    }

    /// <summary>Adds a caret at <paramref name="position"/> (Alt+click), which becomes the primary; a click on an existing caret (of several) removes it.</summary>
    public bool AddCaret(int position)
    {
        position = Math.Clamp(position, 0, Snapshot.Length);
        var current = Selections;
        for (int i = 0; i < current.Length; i++)
        {
            if (current[i].IsEmpty && current[i].Caret == position)
            {
                if (current.Length > 1)
                {
                    RemoveSelectionAt(i);
                }

                return true;
            }
        }

        return AddSelection(SelectionState.At(position));
    }

    /// <summary>Adds <paramref name="selection"/> as a new primary selection (merged with any it overlaps); false at the caret limit.</summary>
    public bool AddSelection(SelectionState selection)
    {
        var current = Selections;
        if (current.Length >= Options.MaxCarets)
        {
            LastCaretMessage = $"At most {Options.MaxCarets} carets (CRAFT_MAX_CARETS).";
            return false;
        }

        LastCaretMessage = null;
        var all = ArrayPool<SelectionState>.Shared.Rent(current.Length + 1);
        try
        {
            int at = 0;
            while (at < current.Length && current[at].Start <= selection.Start)
            {
                at++;
            }

            current[..at].CopyTo(all);
            all[at] = selection;
            current[at..].CopyTo(all.AsSpan(at + 1));
            SetSelectionsCore(all.AsSpan(0, current.Length + 1), at, keepDesiredColumns: false, keepAutoClosed: false, keepBox: false);
        }
        finally
        {
            ArrayPool<SelectionState>.Shared.Return(all);
        }

        EnsureCaretVisible();
        return true;
    }

    /// <summary>Back to the primary caret alone (Escape); false when there was one.</summary>
    public bool CollapseCarets()
    {
        if (_carets.Count <= 1)
        {
            return false;
        }

        SetSelection(_selection);
        return true;
    }

    /// <summary>
    /// Ctrl+D (Visual Studio's Shift+Alt+.): with nothing selected, selects the word at the caret; with a selection, adds
    /// the next place of its text (after the primary, wrapping round) as a new caret. A word taken this way matches whole
    /// words only, as VS Code does; a selection the user made matches anywhere. Case is matched.
    /// </summary>
    public bool AddNextOccurrence()
    {
        var primary = _selection;
        if (primary.IsEmpty)
        {
            var (start, end) = WordAt(primary.Caret);
            if (end <= start || TextReading.KindOf(Snapshot[start]) != CharKind.Word)
            {
                return true;
            }

            ReplacePrimary(new SelectionState(start, end));
            _occurrenceWhole = true;
            EnsureCaretVisible();
            return true;
        }

        string query = Snapshot.GetText(primary.Start, primary.Length);
        bool whole = _occurrenceWhole;
        int found = NextMatch(query, primary.End, whole);
        while (found >= 0 && IsSelectedAt(found))
        {
            if (found == primary.Start)
            {
                return true;    // round to where it started: every place is selected
            }

            found = NextMatch(query, found + 1, whole);
        }

        if (found >= 0)
        {
            AddSelection(new SelectionState(found, found + query.Length));
            _occurrenceWhole = whole;
        }

        return true;
    }

    /// <summary>
    /// Ctrl+Shift+L (VS Code): a selection at every place of the selected text (or of the word at the caret, whole words),
    /// up to <see cref="Core.CraftOptions.MaxCarets"/>.
    /// </summary>
    public bool SelectAllOccurrences()
    {
        var primary = _selection;
        bool whole = _occurrenceWhole;
        if (primary.IsEmpty)
        {
            var (start, end) = WordAt(primary.Caret);
            if (end <= start || TextReading.KindOf(Snapshot[start]) != CharKind.Word)
            {
                return true;
            }

            primary = new SelectionState(start, end);
            whole = true;
        }

        string query = Snapshot.GetText(primary.Start, primary.Length);
        var found = new List<SelectionState>();
        int primaryIndex = 0;
        int at = NextMatch(query, 0, whole, wrap: false);
        while (at >= 0)
        {
            if (found.Count >= Options.MaxCarets)
            {
                LastCaretMessage = $"At most {Options.MaxCarets} carets (CRAFT_MAX_CARETS): the first {found.Count} places are selected.";
                break;
            }

            if (at == primary.Start)
            {
                primaryIndex = found.Count;
            }

            found.Add(new SelectionState(at, at + query.Length));
            at = NextMatch(query, at + Math.Max(1, query.Length), whole, wrap: false);
        }

        if (found.Count == 0)
        {
            return true;
        }

        SetSelectionsCore(CollectionsMarshal.AsSpan(found), primaryIndex, keepDesiredColumns: false, keepAutoClosed: false, keepBox: false);
        _occurrenceWhole = whole;
        EnsureCaretVisible();
        return true;
    }

    /// <summary>Adds a caret on the line above the top caret or below the bottom one, at the primary's column (Ctrl+Alt+Up and Down).</summary>
    public bool AddCaretVertical(LineDirection direction)
    {
        var all = Selections;
        var edge = direction == LineDirection.Up ? all[0] : all[^1];
        var snapshot = Snapshot;
        int line = snapshot.GetLineIndex(edge.Caret);
        int row = TextRowOf(line) + (direction == LineDirection.Up ? -1 : 1);
        if (row < 0 || row >= TextRowCount)
        {
            return true;
        }

        int column = ColumnOf(_selection.Caret);
        int position = PositionAtColumn(snapshot.GetLine(TextLineAt(row)), column, nearest: false);
        return AddSelection(SelectionState.At(position)) || true;
    }

    /// <summary>
    /// Grows or moves a column (box) selection one step (Shift+Alt+arrows in the Visual Studio keymap, Ctrl+Shift+Alt+arrows
    /// in VS Code's): from the primary caret's line and column, a selection on each line of the box.
    /// </summary>
    public bool ColumnSelect(EditorKey key)
    {
        if (!_boxActive)
        {
            var snapshot = Snapshot;
            _boxAnchorLine = snapshot.GetLineIndex(_selection.Anchor);
            _boxAnchorColumn = ColumnOf(_selection.Anchor);
            _boxLine = snapshot.GetLineIndex(_selection.Caret);
            _boxColumn = ColumnOf(_selection.Caret);
        }

        switch (key)
        {
            case EditorKey.Up:
                _boxLine = TextLineAt(Math.Max(0, TextRowOf(_boxLine) - 1));
                break;
            case EditorKey.Down:
                _boxLine = TextLineAt(Math.Min(TextRowCount - 1, TextRowOf(_boxLine) + 1));
                break;
            case EditorKey.PageUp:
                _boxLine = TextLineAt(Math.Max(0, TextRowOf(_boxLine) - PageLines()));
                break;
            case EditorKey.PageDown:
                _boxLine = TextLineAt(Math.Min(TextRowCount - 1, TextRowOf(_boxLine) + PageLines()));
                break;
            case EditorKey.Left:
                _boxColumn = Math.Max(0, _boxColumn - 1);
                break;
            case EditorKey.Right:
                _boxColumn++;
                break;
            default:
                return false;
        }

        ApplyBox();
        return true;
    }

    /// <summary>Starts a column selection at a point of the view (Shift+Alt+press): the box's fixed corner.</summary>
    internal void StartBox(float viewX, float viewY)
    {
        var (line, column) = LineAndColumnFromView(viewX, viewY);
        _boxAnchorLine = _boxLine = line;
        _boxAnchorColumn = _boxColumn = column;
        _pointerMode = PointerMode.Box;
        ApplyBox();
    }

    // ---- the per-caret edit engine ----

    /// <summary>One caret's part of a change: an edit of the text before it, and the caret's selection after it, as offsets from the edit's start in the new text.</summary>
    private struct CaretPart
    {
        public int Start;
        public int Length;
        public string Text;
        public int Anchor;
        public int Caret;
        public int AutoClose;    // offset of an auto-inserted close from Start in the new text, or -1

        public CaretPart(int start, int length, string text, int anchor, int caret)
        {
            Start = start;
            Length = length;
            Text = text;
            Anchor = anchor;
            Caret = caret;
            AutoClose = -1;
        }

        public static CaretPart Delete(int start, int end) => new(start, end - start, "", 0, 0);

        public static CaretPart Stay(SelectionState selection) => new(selection.Start, 0, "", selection.Anchor - selection.Start, selection.Caret - selection.Start);
    }

    /// <summary>
    /// Applies the carets' parts (in order of position) as one change, one undo step; the selections after it are the
    /// parts' own. Overlapping parts are trimmed to start where the one before ended. A part that changes nothing moves only
    /// its caret. Tracked auto-closed brackets follow the change when <paramref name="keepAutoClosed"/>.
    /// </summary>
    private void ApplyParts(Span<CaretPart> parts, EditKind kind, bool keepAutoClosed)
    {
        int count = parts.Length;
        int primary = Math.Min(PrimarySelectionIndex, count - 1);
        var edits = ArrayPool<TextEdit>.Shared.Rent(count);
        var after = ArrayPool<SelectionState>.Shared.Rent(count);
        try
        {
            int edited = 0;
            int delta = 0;
            int lastEnd = 0;
            for (int i = 0; i < count; i++)
            {
                ref var part = ref parts[i];
                if (part.Start < lastEnd)
                {
                    int cut = lastEnd - part.Start;
                    part.Start = lastEnd;
                    part.Length = Math.Max(0, part.Length - cut);
                    part.Anchor = Math.Max(0, part.Anchor - cut);
                    part.Caret = Math.Max(0, part.Caret - cut);
                }

                int start = part.Start + delta;
                after[i] = new SelectionState(start + part.Anchor, start + part.Caret);
                if (part.Length > 0 || part.Text.Length > 0)
                {
                    edits[edited++] = new TextEdit(part.Start, part.Length, part.Text);
                }

                delta += part.Text.Length - part.Length;
                lastEnd = part.Start + part.Length;
            }

            var editSpan = edits.AsSpan(0, edited);
            if (keepAutoClosed && _autoClosed.Count > 0)
            {
                MapAutoClosed(editSpan);
            }

            if (edited == 0)
            {
                SetSelectionsCore(after.AsSpan(0, count), primary, keepDesiredColumns: false, keepAutoClosed: keepAutoClosed, keepBox: false);
            }
            else if (count == 1)
            {
                ApplyEdits(editSpan, kind, after[0], keepAutoClosed);
            }
            else
            {
                ApplyEdits(editSpan, kind, after.AsSpan(0, count), primary, keepAutoClosed);
            }

            // The brackets this change closed, in the new text's positions.
            delta = 0;
            for (int i = 0; i < count; i++)
            {
                ref var part = ref parts[i];
                if (part.AutoClose >= 0)
                {
                    InsertAutoClosed(part.Start + delta + part.AutoClose);
                }

                delta += part.Text.Length - part.Length;
            }
        }
        finally
        {
            ArrayPool<TextEdit>.Shared.Return(edits, clearArray: true);
            ArrayPool<SelectionState>.Shared.Return(after);
        }
    }

    /// <summary>Applies <paramref name="edits"/> with several selections after it (one undo step restoring them all where the document keeps them).</summary>
    internal void ApplyEdits(ReadOnlySpan<TextEdit> edits, EditKind kind, ReadOnlySpan<SelectionState> after, int primaryAfter, bool keepAutoClosed = false) =>
        CommitEdits(edits, kind, after, primaryAfter, keepAutoClosed);

    /// <summary>
    /// The one place every edit of this editor goes to the document (typing, deleting, paste, the line commands, a
    /// completion, a format, an accepted inline completion; one caret or many): a read-only check belongs here. One undo
    /// step; the selections after it are set once the document has changed.
    /// </summary>
    private void CommitEdits(ReadOnlySpan<TextEdit> edits, EditKind kind, ReadOnlySpan<SelectionState> after, int primaryAfter, bool keepAutoClosed)
    {
        if (_readOnly)
        {
            RefusedEdits++;                                  // read-only (the owner's note 26): refused here, the selection unmoved
            return;
        }

        _applying = true;
        _applyingAfter = after[primaryAfter];
        try
        {
            if (after.Length == 1 && !HasMultipleCarets)
            {
                _document.Edit(edits, kind, _selection, after[0]);
            }
            else if (_document is IMultiSelectionDocument multi)
            {
                multi.Edit(edits, kind, Selections, PrimarySelectionIndex, after, primaryAfter);
            }
            else
            {
                _document.Edit(edits, kind, _selection, after[primaryAfter]);
            }
        }
        finally
        {
            _applying = false;
            _applyingAfter = null;
        }

        if (after.Length == 1)
        {
            _occurrenceWhole = false;
        }

        SetSelectionsCore(after, primaryAfter, keepDesiredColumns: false, keepAutoClosed: keepAutoClosed, keepBox: false);
    }

    /// <summary>Runs <paramref name="plan"/> for every caret and applies the parts as one change.</summary>
    private void EditCarets<TState>(EditKind kind, bool keepAutoClosed, TState state, CaretPlan<TState> plan)
    {
        var selections = Selections;
        int count = selections.Length;
        var parts = ArrayPool<CaretPart>.Shared.Rent(count);
        try
        {
            var snapshot = Snapshot;
            for (int i = 0; i < count; i++)
            {
                parts[i] = plan(this, selections[i], snapshot, state);
            }

            ApplyParts(parts.AsSpan(0, count), kind, keepAutoClosed);
        }
        finally
        {
            ArrayPool<CaretPart>.Shared.Return(parts, clearArray: true);
        }
    }

    private delegate CaretPart CaretPlan<TState>(EditorModel model, SelectionState selection, ITextSnapshot snapshot, TState state);

    // Sets the selections: clamped, sorted, merged where they overlap (or are the same caret). The primary follows its
    // selection through the sort and the merge. With one left, the editor is back to a single caret.
    private void SetSelectionsCore(ReadOnlySpan<SelectionState> selections, int primary, bool keepDesiredColumns, bool keepAutoClosed, bool keepBox)
    {
        int length = Snapshot.Length;
        var previous = _selection;
        int previousCount = SelectionCount;
        if (!keepBox)
        {
            _boxActive = false;
        }

        if (!keepAutoClosed)
        {
            _autoClosed.Clear();
        }

        if (selections.Length == 1)
        {
            _carets.Clear();
            SetPrimary(Clamp(selections[0], length), keepDesiredColumns, previousCount > 1);
            return;
        }

        _carets.Clear();
        bool sorted = true;
        for (int i = 0; i < selections.Length; i++)
        {
            var s = Clamp(selections[i], length);
            if (i > 0 && s.Start < _carets[^1].Start)
            {
                sorted = false;
            }

            _carets.Add(s);
        }

        var primaryValue = _carets[primary];
        if (!sorted)
        {
            _carets.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
            primary = _carets.IndexOf(primaryValue);
        }

        // Merge: one pass, in place; the primary goes where its selection lands.
        int kept = 0;
        int landed = 0;
        for (int i = 0; i < _carets.Count; i++)
        {
            var s = _carets[i];
            if (kept > 0)
            {
                var last = _carets[kept - 1];
                if (s.Start < last.End || (s.Start == last.Start && (s.IsEmpty || last.IsEmpty)))
                {
                    int start = Math.Min(last.Start, s.Start);
                    int end = Math.Max(last.End, s.End);
                    bool forward = last.Caret >= last.Anchor;
                    _carets[kept - 1] = forward ? new SelectionState(start, end) : new SelectionState(end, start);
                    if (i == primary)
                    {
                        landed = kept - 1;
                    }

                    continue;
                }
            }

            if (i == primary)
            {
                landed = kept;
            }

            _carets[kept++] = s;
        }

        primary = landed;
        _carets.RemoveRange(kept, _carets.Count - kept);
        if (_carets.Count == 1)
        {
            var only = _carets[0];
            _carets.Clear();
            SetPrimary(only, keepDesiredColumns, previousCount > 1);
            return;
        }

        if (!keepDesiredColumns || _desiredCount != _carets.Count)
        {
            _desiredCount = 0;
        }

        _desiredColumn = keepDesiredColumns ? _desiredColumn : -1;
        _primary = primary;
        _selection = _carets[primary];
        SelectionStamp++;
        RevealCarets();
        if (_selection != previous || previousCount != _carets.Count)
        {
            CaretMoved?.Invoke(this, EventArgs.Empty);
        }

        Invalidate();
    }

    private void SetPrimary(SelectionState selection, bool keepDesiredColumn, bool hadMany)
    {
        if (!keepDesiredColumn)
        {
            _desiredColumn = -1;
        }

        _desiredCount = 0;
        bool moved = selection != _selection || hadMany;
        _selection = selection;
        SelectionStamp++;
        RevealCarets();
        if (moved)
        {
            CaretMoved?.Invoke(this, EventArgs.Empty);
        }

        Invalidate();
    }

    private static SelectionState Clamp(SelectionState s, int length) => new(Math.Clamp(s.Anchor, 0, length), Math.Clamp(s.Caret, 0, length));

    // A caret the folds hide unfolds them (a search, go to line, Ctrl+D, a click or a key took it there).
    private void RevealCarets()
    {
        if (Folding is not { } folding || !folding.Map.HasHidden)
        {
            return;
        }

        var snapshot = Snapshot;
        bool changed = false;
        foreach (var s in Selections)
        {
            changed |= folding.Map.Reveal(snapshot.GetLineIndex(s.Caret));
        }

        if (changed)
        {
            OnFoldsChanged();
        }
    }

    private void ReplacePrimary(SelectionState selection)
    {
        if (_carets.Count == 0)
        {
            SetSelection(selection);
            return;
        }

        var all = ArrayPool<SelectionState>.Shared.Rent(_carets.Count);
        try
        {
            CollectionsMarshal.AsSpan(_carets).CopyTo(all);
            all[_primary] = selection;
            SetSelectionsCore(all.AsSpan(0, _carets.Count), _primary, keepDesiredColumns: false, keepAutoClosed: false, keepBox: false);
        }
        finally
        {
            ArrayPool<SelectionState>.Shared.Return(all);
        }
    }

    private void RemoveSelectionAt(int index)
    {
        var all = ArrayPool<SelectionState>.Shared.Rent(_carets.Count);
        try
        {
            int n = 0;
            for (int i = 0; i < _carets.Count; i++)
            {
                if (i != index)
                {
                    all[n++] = _carets[i];
                }
            }

            int primary = _primary == index ? n - 1 : _primary > index ? _primary - 1 : _primary;
            SetSelectionsCore(all.AsSpan(0, n), primary, keepDesiredColumns: false, keepAutoClosed: false, keepBox: false);
        }
        finally
        {
            ArrayPool<SelectionState>.Shared.Return(all);
        }
    }

    private bool IsSelectedAt(int start)
    {
        foreach (var s in Selections)
        {
            if (s.Start == start)
            {
                return true;
            }

            if (s.Start > start)
            {
                break;
            }
        }

        return false;
    }

    // The first place of `query` at or after `from` (wrapping round to the start when `wrap`), or -1; whole words only
    // when `whole`. A query on one line is searched a line at a time; one over several lines in the whole text.
    private int NextMatch(string query, int from, bool whole, bool wrap = true)
    {
        var snapshot = Snapshot;
        if (query.Length == 0 || query.Length > snapshot.Length)
        {
            return -1;
        }

        from = Math.Clamp(from, 0, snapshot.Length);
        if (query.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            string text = snapshot.GetText(0, snapshot.Length);
            int at = from;
            while ((at = text.IndexOf(query, at, StringComparison.Ordinal)) >= 0)
            {
                if (!whole || IsWholeWord(snapshot, at, query.Length))
                {
                    return at;
                }

                at++;
            }

            if (!wrap || from == 0)
            {
                return -1;
            }

            int again = NextMatch(query, 0, whole, wrap: false);
            return again >= 0 && again < from ? again : -1;
        }

        char[]? buffer = null;
        try
        {
            int lines = snapshot.LineCount;
            int first = snapshot.GetLineIndex(from);
            for (int step = 0; step <= lines; step++)
            {
                int index = first + step;
                if (index >= lines)
                {
                    if (!wrap)
                    {
                        return -1;
                    }

                    index -= lines;
                }

                var line = snapshot.GetLine(index);
                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                int offset = step == 0 ? from - line.Start : 0;
                if (step == lines && index == first)
                {
                    text = text[..Math.Min(text.Length, Math.Max(0, from - line.Start + query.Length - 1))];
                    offset = 0;
                }

                while (offset <= text.Length - query.Length)
                {
                    int found = text[offset..].IndexOf(query.AsSpan(), StringComparison.Ordinal);
                    if (found < 0)
                    {
                        break;
                    }

                    int at = line.Start + offset + found;
                    if (!whole || IsWholeWord(snapshot, at, query.Length))
                    {
                        return at;
                    }

                    offset += found + 1;
                }
            }

            return -1;
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    private static bool IsWholeWord(ITextSnapshot snapshot, int start, int length)
    {
        int end = start + length;
        return (start == 0 || TextReading.KindOf(snapshot[start - 1]) != CharKind.Word)
            && (end >= snapshot.Length || TextReading.KindOf(snapshot[end]) != CharKind.Word);
    }

    private (int Line, int Column) LineAndColumnFromView(float viewX, float viewY)
    {
        int row = Math.Max(0, (int)MathF.Floor((viewY + ScrollY) / Metrics.LineHeight));
        int line = LineAtRow(Math.Min(row, RowCount - 1));
        float x = Math.Max(0, viewX + ScrollX - TextLeft);
        int column = (int)MathF.Round(x / Metrics.CellWidth);
        return (line, column);
    }

    /// <summary>The pointer moved while a Shift+Alt drag holds a column selection: the box's moving corner follows it.</summary>
    private void MoveBox(float viewX, float viewY)
    {
        var (line, column) = LineAndColumnFromView(viewX, viewY);
        if (line == _boxLine && column == _boxColumn && _boxActive)
        {
            return;
        }

        _boxLine = line;
        _boxColumn = column;
        ApplyBox();
    }

    // The box's selections: on each shown line from the anchor's to the caret's, from the anchor's column to the caret's
    // (each clamped to its line), the primary on the caret's line.
    private void ApplyBox()
    {
        var snapshot = Snapshot;
        int top = Math.Min(_boxAnchorLine, _boxLine);
        int bottom = Math.Max(_boxAnchorLine, _boxLine);
        int rows = Math.Min(Options.MaxCarets, RowOfLine(bottom) - RowOfLine(top) + 1);
        var all = ArrayPool<SelectionState>.Shared.Rent(Math.Max(1, rows));
        try
        {
            int n = 0;
            int primary = 0;
            for (int line = top; line <= bottom && n < rows; line = NextShownLine(line))
            {
                var text = snapshot.GetLine(line);
                int anchor = PositionAtColumn(text, _boxAnchorColumn, nearest: false);
                int caret = PositionAtColumn(text, _boxColumn, nearest: false);
                if (line == _boxLine)
                {
                    primary = n;
                }

                all[n++] = new SelectionState(anchor, caret);
            }

            if (n < bottom - top + 1 && rows == Options.MaxCarets)
            {
                LastCaretMessage = $"At most {Options.MaxCarets} carets (CRAFT_MAX_CARETS).";
            }

            SetSelectionsCore(all.AsSpan(0, n), primary, keepDesiredColumns: false, keepAutoClosed: false, keepBox: true);
            _boxActive = true;
        }
        finally
        {
            ArrayPool<SelectionState>.Shared.Return(all);
        }

        EnsureCaretVisible();
    }

    private int PageLines() => Math.Max(1, (int)MathF.Floor(ViewportHeight / Metrics.LineHeight));

    // ---- auto-closed brackets (kept sorted) ----

    private bool IsAutoClosed(int position) => AutoClosedIndex(position) >= 0;

    private int AutoClosedIndex(int position)
    {
        int lo = 0, hi = _autoClosed.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int value = _autoClosed[mid];
            if (value == position)
            {
                return mid;
            }

            if (value < position)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return ~lo;
    }

    private void InsertAutoClosed(int position)
    {
        int at = AutoClosedIndex(position);
        if (at < 0)
        {
            _autoClosed.Insert(~at, position);
        }
    }

    private void RemoveAutoClosed(int position)
    {
        int at = AutoClosedIndex(position);
        if (at >= 0)
        {
            _autoClosed.RemoveAt(at);
        }
    }

    // The tracked closes follow `edits` (in the text before them): one merge walk of the two sorted lists. A close the
    // change removed is forgotten; one at an insertion's position moves after it.
    private void MapAutoClosed(ReadOnlySpan<TextEdit> edits)
    {
        int kept = 0;
        int e = 0;
        int delta = 0;
        for (int i = 0; i < _autoClosed.Count; i++)
        {
            int p = _autoClosed[i];
            while (e < edits.Length && (edits[e].End < p || (edits[e].End == p && (edits[e].Length > 0 || edits[e].Start < p)) || (edits[e].Length == 0 && edits[e].Start == p)))
            {
                delta += edits[e].Delta;
                e++;
            }

            if (e < edits.Length && edits[e].Start <= p && p < edits[e].End)
            {
                continue;       // deleted
            }

            _autoClosed[kept++] = p + delta;
        }

        _autoClosed.RemoveRange(kept, _autoClosed.Count - kept);
    }
}
