// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Editor;

public sealed partial class EditorModel
{
    private readonly List<int> _autoClosed = [];
    private string[]? _pairTexts;
    private long _lastClickTimestamp;
    private float _lastClickX = float.NaN;
    private float _lastClickY = float.NaN;
    private int _clickCount;
    private bool _pointerDown;

    /// <summary>How many clicks the last press counted (1, 2 for a word, 3 for a line).</summary>
    public int ClickCount => _clickCount;

    /// <inheritdoc />
    public bool OnKey(EditorKey key, KeyModifiers modifiers)
    {
        bool shift = (modifiers & KeyModifiers.Shift) != 0;
        bool control = (modifiers & KeyModifiers.Control) != 0;
        bool alt = (modifiers & KeyModifiers.Alt) != 0;
        if (InlineChatKey(key, modifiers))
        {
            NoteInput();
            return true;
        }

        if (!Completion.IsOpen && CompletesHere && InlineCompletion.HandleKey(key, modifiers))
        {
            NoteInput();
            return true;
        }

        if (OnSemanticKey(key, modifiers))
        {
            NoteInput();
            return true;
        }

        if (alt && key is not (EditorKey.Left or EditorKey.Right or EditorKey.Up or EditorKey.Down))
        {
            return false;
        }

        NoteInput();
        bool handled = key switch
        {
            EditorKey.Left or EditorKey.Right or EditorKey.Up or EditorKey.Down or EditorKey.Home or EditorKey.End
                or EditorKey.PageUp or EditorKey.PageDown => Move(key, shift, control),
            EditorKey.Backspace => Backspace(control),
            EditorKey.Delete => DeleteForward(control),
            EditorKey.Enter when !control => Enter(),
            EditorKey.Tab when !control => shift ? OutdentLines() : Indent(),
            EditorKey.Escape => Escape(),
            EditorKey.Insert when !shift && !control => ToggleOverwrite(),
            EditorKey.F3 => shift ? Find.FindPrevious() || true : Find.FindNext() || true,
            EditorKey.A when control => SelectAll(),
            EditorKey.C when control => Copy(),
            EditorKey.X when control => _readOnly ? Refuse() : Cut(),
            EditorKey.V when control => _readOnly ? Refuse() : Paste(),
            EditorKey.Z when control => shift ? Redo() : Undo(),
            EditorKey.Y when control => Redo(),
            EditorKey.S when control && !_readOnly => Raise(SaveRequested),
            EditorKey.F when control => RaiseFind(replace: false),
            EditorKey.H when control => RaiseFind(replace: true),
            _ => false,
        };

        if (!handled)
        {
            _inputPending = false;
        }
        else
        {
            AfterKey(key);
        }

        return handled;
    }

    /// <inheritdoc />
    public void OnText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (InlineChatText(text))
        {
            NoteInput();
            return;                                          // the inline chat's box has the keyboard
        }

        if (_readOnly)
        {
            RefusedEdits++;                                  // read-only: typed text goes nowhere (nothing to complete either)
            return;
        }

        NoteInput();
        // The X restart, piece 2b: with CRAFT_COMPLETION_ALL_CARETS, several carets keep the completion list and ghost text
        // (asked at the primary); off, they close while there are several.
        bool completes = CompletesHere;
        if (!completes)
        {
            InlineCompletion.Dismiss();
        }
        else
        {
            InlineCompletion.TypeThrough(text);
        }

        EditCarets(EditKind.Typing, keepAutoClosed: true, text, static (model, selection, snapshot, typed) => model.TypedPart(selection, snapshot, typed));
        EnsureCaretVisible();
        if (completes)
        {
            AfterTyped(text);
        }
    }

    private SelectionState _selectionBeforePress;

    /// <summary>
    /// The context menu opened (a right-click) at the press that just happened: a press inside the selection keeps it (so
    /// Cut, Copy and the AI actions act on it), as it was before the press; a press outside it keeps the caret where it
    /// went. True when the selection was kept.
    /// </summary>
    public bool KeepSelectionForContextMenu()
    {
        _pointerDown = false;
        var before = _selectionBeforePress;
        int caret = _selection.Caret;
        if (before.Anchor == before.Caret || caret < Math.Min(before.Anchor, before.Caret) || caret > Math.Max(before.Anchor, before.Caret))
        {
            return false;
        }

        SetSelection(before);
        return true;
    }

    /// <summary>
    /// A press of the primary button at a point of the view (from its top-left; the scroll offsets are added here). Shift
    /// extends the selection; a second and third press close in time and place select the word and the line.
    /// </summary>
    public void PointerPressed(float viewX, float viewY, KeyModifiers modifiers)
    {
        NoteInput();
        long now = _time.GetTimestamp();
        float distance = Options.DoubleClickDistance;
        bool near = Math.Abs(viewX - _lastClickX) <= distance && Math.Abs(viewY - _lastClickY) <= distance;
        bool soon = _clickCount > 0 && _time.GetElapsedTime(_lastClickTimestamp, now).TotalMilliseconds <= Options.DoubleClickMilliseconds;
        _clickCount = near && soon ? (_clickCount % 3) + 1 : 1;
        _lastClickTimestamp = now;
        _lastClickX = viewX;
        _lastClickY = viewY;
        _pointerDown = true;
        _selectionBeforePress = _selection;
        NotePress(viewX, viewY, modifiers);
        InlineCompletion.Dismiss();
        if (InlineChatPress(viewX, viewY))
        {
            _pointerDown = false;
            return;
        }

        if (PopupRowAt(viewX, viewY) is { } row)
        {
            _pointerDown = false;
            Completion.Click(row, _clickCount);
            return;
        }

        if (PressOnLayers(viewX, viewY, modifiers))
        {
            return;
        }

        CloseTooltips();
        _completion?.Close();
        _pointerMode = PointerMode.Select;
        if (_lightbulb is { } bulb && bulb.HitTest(viewX, viewY))
        {
            _pointerDown = false;
            bulb.Press();
            return;
        }

        if (PressOnLineMark(viewX, viewY))
        {
            return;
        }

        if (FoldChevronAt(viewX, viewY) is int foldLine)
        {
            _pointerDown = false;
            Folding.Toggle(foldLine);
            return;
        }

        bool shiftHeld = (modifiers & KeyModifiers.Shift) != 0;
        bool altHeld = (modifiers & KeyModifiers.Alt) != 0;
        bool controlHeld = (modifiers & KeyModifiers.Control) != 0;
        if (altHeld && shiftHeld && !controlHeld)
        {
            // Shift+Alt+press: a column selection from here, the drag its other corner.
            _clickCount = 1;
            StartBox(viewX, viewY);
            return;
        }

        int position = PositionFromView(viewX, viewY);
        if (altHeld && !shiftHeld && !controlHeld)
        {
            // Alt+click: another caret (a click on one of several takes it away); a drag selects from it.
            _clickCount = 1;
            AddCaret(position);
            _pointerMode = PointerMode.AddSelection;
            return;
        }

        if (shiftHeld && _clickCount == 1)
        {
            SetSelection(new SelectionState(_selection.Anchor, position));
            return;
        }

        switch (_clickCount)
        {
            case 2:
                var (start, end) = WordAt(position);
                SetSelection(new SelectionState(start, end));
                break;
            case 3:
                var snapshot = Snapshot;
                var line = snapshot.GetLine(snapshot.GetLineIndex(position));
                SetSelection(new SelectionState(line.Start, line.EndIncludingBreak));
                break;
            default:
                SetSelection(SelectionState.At(position));
                break;
        }
    }

    /// <summary>The pointer moved to a point of the view; while pressed, the selection follows it.</summary>
    public void PointerMoved(float viewX, float viewY)
    {
        _pointerX = viewX;
        _pointerY = viewY;
        if (!_pointerDown || _clickCount != 1)
        {
            return;
        }

        NoteInput();
        if (MoveOnLayers(viewX, viewY))
        {
            return;
        }

        switch (_pointerMode)
        {
            case PointerMode.Box:
                MoveBox(viewX, viewY);
                return;
            case PointerMode.AddSelection when HasMultipleCarets:
                ReplacePrimary(new SelectionState(_selection.Anchor, PositionFromView(viewX, viewY)));
                EnsureCaretVisible();
                return;
        }

        SetSelection(new SelectionState(_selection.Anchor, PositionFromView(viewX, viewY)));
        EnsureCaretVisible();
    }

    /// <summary>The primary button was released.</summary>
    public void PointerReleased()
    {
        _pointerDown = false;
        _pointerMode = PointerMode.Select;
        _cardSelecting = false;
        NoteRelease();
    }

    private int PositionFromView(float viewX, float viewY)
    {
        float x = viewX < TextLeft ? TextLeft : viewX + ScrollX;
        return PositionFromPoint(x, viewY + ScrollY);
    }

    // ---- movement ----

    private bool Move(EditorKey key, bool shift, bool control)
    {
        bool vertical = key is EditorKey.Up or EditorKey.Down or EditorKey.PageUp or EditorKey.PageDown;
        if (key is EditorKey.PageUp or EditorKey.PageDown && ViewportHeight > 0)
        {
            int sign = key == EditorKey.PageUp ? -1 : 1;
            float y = Math.Clamp(ScrollY + (sign * PageLines() * Metrics.LineHeight), 0, Math.Max(0, ContentHeight - ViewportHeight));
            if (y != ScrollY)
            {
                ScrollTo(ScrollX, y);
            }
        }

        if (!HasMultipleCarets)
        {
            var selection = _selection;
            int target = MoveTarget(selection, key, shift, control, ref _desiredColumn);
            SetSelection(shift ? new SelectionState(selection.Anchor, target) : SelectionState.At(target), keepDesiredColumn: vertical);
            EnsureCaretVisible();
            return true;
        }

        int count = _carets.Count;
        if (_desiredColumns.Length < count)
        {
            Array.Resize(ref _desiredColumns, Math.Max(count, _desiredColumns.Length * 2));
        }

        bool keep = vertical && _desiredCount == count;
        var after = ArrayPool<SelectionState>.Shared.Rent(count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                var selection = _carets[i];
                int desired = keep ? _desiredColumns[i] : -1;
                int target = MoveTarget(selection, key, shift, control, ref desired);
                _desiredColumns[i] = desired;
                after[i] = shift ? new SelectionState(selection.Anchor, target) : SelectionState.At(target);
            }

            _desiredCount = vertical ? count : 0;
            SetSelectionsCore(after.AsSpan(0, count), _primary, keepDesiredColumns: vertical, keepAutoClosed: false, keepBox: false);
        }
        finally
        {
            ArrayPool<SelectionState>.Shared.Return(after);
        }

        EnsureCaretVisible();
        return true;
    }

    // Where one caret goes for a movement key; a vertical move keeps `desired` (the column it started from).
    private int MoveTarget(SelectionState selection, EditorKey key, bool shift, bool control, ref int desired)
    {
        var snapshot = Snapshot;
        int caret = selection.Caret;
        switch (key)
        {
            case EditorKey.Left:
                return !shift && !selection.IsEmpty ? selection.Start : control ? WordLeft(caret) : CharLeft(caret);
            case EditorKey.Right:
                return !shift && !selection.IsEmpty ? selection.End : control ? WordRight(caret) : CharRight(caret);
            case EditorKey.Up:
            case EditorKey.Down:
                return LineMove(caret, key == EditorKey.Up ? -1 : 1, ref desired);
            case EditorKey.PageUp:
            case EditorKey.PageDown:
                return LineMove(caret, (key == EditorKey.PageUp ? -1 : 1) * PageLines(), ref desired);
            case EditorKey.Home:
                return control ? 0 : WrappedHome(caret) ?? SmartHome(caret);
            default:
                return control ? snapshot.Length : WrappedEnd(caret) ?? snapshot.GetLine(snapshot.GetLineIndex(caret)).End;
        }
    }

    private int CharLeft(int position)
    {
        if (position <= 0)
        {
            return 0;
        }

        var snapshot = Snapshot;
        var line = snapshot.GetLine(snapshot.GetLineIndex(position));
        if (position == line.Start)
        {
            return snapshot.GetLine(line.Index - 1).End;
        }

        return position >= 2 && char.IsLowSurrogate(snapshot[position - 1]) && char.IsHighSurrogate(snapshot[position - 2]) ? position - 2 : position - 1;
    }

    private int CharRight(int position)
    {
        var snapshot = Snapshot;
        if (position >= snapshot.Length)
        {
            return snapshot.Length;
        }

        var line = snapshot.GetLine(snapshot.GetLineIndex(position));
        if (position >= line.End)
        {
            return line.Index + 1 < snapshot.LineCount ? snapshot.GetLine(line.Index + 1).Start : line.End;
        }

        return position + 1 < line.End && char.IsHighSurrogate(snapshot[position]) && char.IsLowSurrogate(snapshot[position + 1]) ? position + 2 : position + 1;
    }

    private int WordRight(int position)
    {
        var snapshot = Snapshot;
        var line = snapshot.GetLine(snapshot.GetLineIndex(Math.Clamp(position, 0, snapshot.Length)));
        if (position >= line.End)
        {
            return line.Index + 1 < snapshot.LineCount ? snapshot.GetLine(line.Index + 1).Start : line.End;
        }

        char[]? buffer = null;
        try
        {
            var text = TextReading.ReadLine(snapshot, line, ref buffer);
            int i = position - line.Start;
            var kind = TextReading.KindOf(text[i]);
            if (kind != CharKind.Space)
            {
                while (i < text.Length && TextReading.KindOf(text[i]) == kind)
                {
                    i++;
                }
            }

            while (i < text.Length && TextReading.KindOf(text[i]) == CharKind.Space)
            {
                i++;
            }

            return line.Start + i;
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    private int WordLeft(int position)
    {
        var snapshot = Snapshot;
        var line = snapshot.GetLine(snapshot.GetLineIndex(Math.Clamp(position, 0, snapshot.Length)));
        if (position <= line.Start)
        {
            return line.Index > 0 ? snapshot.GetLine(line.Index - 1).End : 0;
        }

        char[]? buffer = null;
        try
        {
            var text = TextReading.ReadLine(snapshot, line, ref buffer);
            int i = Math.Min(position - line.Start, text.Length);
            while (i > 0 && TextReading.KindOf(text[i - 1]) == CharKind.Space)
            {
                i--;
            }

            if (i > 0)
            {
                var kind = TextReading.KindOf(text[i - 1]);
                while (i > 0 && TextReading.KindOf(text[i - 1]) == kind)
                {
                    i--;
                }
            }

            return line.Start + i;
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    private (int Start, int End) WordAt(int position)
    {
        var snapshot = Snapshot;
        var line = snapshot.GetLine(snapshot.GetLineIndex(position));
        if (line.Length == 0)
        {
            return (position, position);
        }

        char[]? buffer = null;
        try
        {
            var text = TextReading.ReadLine(snapshot, line, ref buffer);
            int i = Math.Min(position - line.Start, text.Length - 1);
            if (position - line.Start == text.Length || (i > 0 && TextReading.KindOf(text[i]) != CharKind.Word && TextReading.KindOf(text[i - 1]) == CharKind.Word))
            {
                i = Math.Max(0, (position - line.Start) - 1);
            }

            var kind = TextReading.KindOf(text[i]);
            int start = i;
            int end = i;
            while (start > 0 && TextReading.KindOf(text[start - 1]) == kind)
            {
                start--;
            }

            while (end < text.Length && TextReading.KindOf(text[end]) == kind)
            {
                end++;
            }

            return (line.Start + start, line.Start + end);
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    private int SmartHome(int position)
    {
        var snapshot = Snapshot;
        var line = snapshot.GetLine(snapshot.GetLineIndex(position));
        char[]? buffer = null;
        try
        {
            var text = TextReading.ReadLine(snapshot, line, ref buffer);
            int firstNonBlank = line.Start + TextReading.IndentLength(text);
            return position == firstNonBlank ? line.Start : firstNonBlank;
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    // A vertical move by rows: folded lines are stepped over, never entered; a wrapped line's rows are each a row
    // (X part 9), the column kept on the screen's grid.
    private int LineMove(int position, int lines, ref int desired)
    {
        var snapshot = Snapshot;
        var (row, column) = VisualOf(position);
        if (desired < 0)
        {
            desired = column;
        }

        int target = row + lines;
        if (target < 0)
        {
            return 0;
        }

        if (target >= RowCount)
        {
            return snapshot.Length;
        }

        return PositionAtVisual(target, desired, nearest: false);
    }

    // Home on a wrapped line's continuation row: the row's start (after its indent); at the row's start, the line's Home.
    private int? WrappedHome(int position)
    {
        if (!WordWrap.IsActive)
        {
            return null;
        }

        var snapshot = Snapshot;
        var line = snapshot.GetLine(snapshot.GetLineIndex(position));
        var wrapped = WordWrap.Line(line.Index);
        if (wrapped is not { Rows: > 1 })
        {
            return null;
        }

        int row = wrapped.RowOf(position - line.Start);
        int start = line.Start + wrapped.RowStart(row);
        return row > 0 && position != start ? start : null;
    }

    // End on a wrapped line's row before its last: the row's end (before its last character, the next row's start being
    // drawn there); at it, the line's end.
    private int? WrappedEnd(int position)
    {
        if (!WordWrap.IsActive)
        {
            return null;
        }

        var snapshot = Snapshot;
        var line = snapshot.GetLine(snapshot.GetLineIndex(position));
        var wrapped = WordWrap.Line(line.Index);
        if (wrapped is not { Rows: > 1 })
        {
            return null;
        }

        int row = wrapped.RowOf(position - line.Start);
        if (row + 1 >= wrapped.Rows)
        {
            return null;
        }

        int end = line.Start + Math.Max(wrapped.RowStart(row), wrapped.RowEnd(row) - 1);
        return position != end ? end : null;
    }

    private bool SelectAll()
    {
        SetSelection(new SelectionState(0, Snapshot.Length));
        return true;
    }

    private bool Escape()
    {
        if (Tooltip is { Kind: EditorTooltipKind.Card })
        {
            CloseTooltips();
            return true;
        }

        if (CollapseCarets())
        {
            return true;
        }

        if (Find.IsHighlighting)
        {
            Find.IsHighlighting = false;
            Invalidate();
            return true;
        }

        if (!_selection.IsEmpty)
        {
            SetSelection(SelectionState.At(_selection.Caret));
            return true;
        }

        return false;
    }

    private bool ToggleOverwrite()
    {
        Overwrite = !Overwrite;
        Invalidate();
        return true;
    }

    // A read-only text's cut or paste: nothing copied away, nothing read from the clipboard.
    private bool Refuse()
    {
        RefusedEdits++;
        return true;
    }

    private bool Raise(EventHandler? handler)
    {
        handler?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool RaiseFind(bool replace)
    {
        string? query = null;
        var selection = _selection;
        if (!selection.IsEmpty && Snapshot.GetLineIndex(selection.Start) == Snapshot.GetLineIndex(selection.End))
        {
            query = Snapshot.GetText(selection.Start, selection.Length);
        }

        FindRequested?.Invoke(this, new FindRequestedEventArgs(replace, query));
        return true;
    }

    // ---- editing ----

    internal void Replace(int start, int length, string text, EditKind kind, SelectionState after, bool keepAutoClosed = false)
    {
        var edit = new TextEdit(start, length, text);
        ApplyEdits(new ReadOnlySpan<TextEdit>(in edit), kind, after, keepAutoClosed);
    }

    // The one place the editor's single-selection edits enter (typing, paste, cut, delete, the line commands, find's
    // replace, formatting, the completions); the multi-caret edits enter through CommitEdits too, which a read-only
    // editor refuses (the owner's note 26).
    internal void ApplyEdits(ReadOnlySpan<TextEdit> edits, EditKind kind, SelectionState after, bool keepAutoClosed = false) =>
        CommitEdits(edits, kind, new ReadOnlySpan<SelectionState>(in after), 0, keepAutoClosed);

    private string NewLine
    {
        get
        {
            if (FileSettings.NewLine is { } fileNewLine)
            {
                return fileNewLine;                               // .editorconfig's end_of_line wins for the breaks the editor inserts
            }

            var snapshot = Snapshot;
            if (snapshot.LineCount > 1)
            {
                var first = snapshot.GetLine(0);
                return first.BreakLength == 2 ? "\r\n" : snapshot[first.End] == '\r' ? "\r" : "\n";
            }

            return Options.NewLine ?? Environment.NewLine;
        }
    }

    private char? CloseOf(char open)
    {
        foreach (var pair in Rules.Brackets)
        {
            if (pair.Open == open)
            {
                return pair.Close;
            }
        }

        return null;
    }

    private char? OpenOf(char close)
    {
        foreach (var pair in Rules.Brackets)
        {
            if (pair.Close == close)
            {
                return pair.Open;
            }
        }

        return null;
    }

    private bool AllowsAutoClose(int position)
    {
        var snapshot = Snapshot;
        if (position >= snapshot.Length)
        {
            return true;
        }

        char next = snapshot[position];
        return next is ' ' or '\t' or '\r' or '\n' || OpenOf(next) is not null || next is ';' or ',';
    }

    // The text of an opening bracket typed with its close ("()"), made once per pair of the language's rules.
    private string? PairText(char open)
    {
        var brackets = Rules.Brackets;
        if (_pairTexts is null || _pairTexts.Length != brackets.Count)
        {
            var texts = new string[brackets.Count];
            for (int i = 0; i < texts.Length; i++)
            {
                texts[i] = string.Create(2, brackets[i], static (span, pair) =>
                {
                    span[0] = pair.Open;
                    span[1] = pair.Close;
                });
            }

            _pairTexts = texts;
        }

        for (int i = 0; i < brackets.Count; i++)
        {
            if (brackets[i].Open == open)
            {
                return _pairTexts[i];
            }
        }

        return null;
    }

    // One caret's typed text: over an auto-inserted close it steps over it; a close bracket typed on a blank line takes
    // its opening line's indent; an opening bracket gets its close (the option, before white space or a close); in
    // overwrite mode it replaces what follows.
    private CaretPart TypedPart(SelectionState selection, ITextSnapshot snapshot, string text)
    {
        if (text.Length == 1 && selection.IsEmpty)
        {
            int caret = selection.Caret;
            char c = text[0];
            if (caret < snapshot.Length && snapshot[caret] == c && OpenOf(c) is not null && IsAutoClosed(caret))
            {
                RemoveAutoClosed(caret);
                return new CaretPart(caret, 0, "", 1, 1);
            }

            if (!Overwrite && OutdentedClose(caret, snapshot, c) is { } outdented)
            {
                return outdented;
            }
        }

        int start = selection.Start;
        int length = selection.Length;
        if (Overwrite && selection.IsEmpty)
        {
            var line = snapshot.GetLine(snapshot.GetLineIndex(start));
            int replace = 0;
            int at = start;
            for (int i = 0; i < text.Length && at < line.End; i++)
            {
                if (char.IsLowSurrogate(text[i]))
                {
                    continue;
                }

                at += at + 1 < line.End && char.IsHighSurrogate(snapshot[at]) && char.IsLowSurrogate(snapshot[at + 1]) ? 2 : 1;
                replace = at - start;
            }

            length = replace;
        }

        if (Options.AutoCloseBrackets && text.Length == 1 && selection.IsEmpty && PairText(text[0]) is { } pair && AllowsAutoClose(start + length))
        {
            return new CaretPart(start, length, pair, 1, 1) { AutoClose = 1 };
        }

        return new CaretPart(start, length, text, text.Length, text.Length);
    }

    // '}' or ']' typed where only white space is before the caret on its line: the line takes the indent of the line that
    // opened the bracket (found by a backward scan of BracketScanLines lines), when that is less, so a block typed with
    // Enter closes where it began. Null when it does not apply.
    private CaretPart? OutdentedClose(int caret, ITextSnapshot snapshot, char close)
    {
        if (close is not ('}' or ']') || OpenOf(close) is not { } open)
        {
            return null;
        }

        var line = snapshot.GetLine(snapshot.GetLineIndex(caret));
        int prefix = caret - line.Start;
        if (prefix == 0)
        {
            return null;
        }

        for (int i = line.Start; i < caret; i++)
        {
            if (snapshot[i] is not (' ' or '\t'))
            {
                return null;
            }
        }

        int floor = snapshot.GetLine(Math.Max(0, line.Index - Options.BracketScanLines)).Start;
        int depth = 0;
        for (int i = line.Start - 1; i >= floor; i--)
        {
            char c = snapshot[i];
            if (c == close)
            {
                depth++;
            }
            else if (c == open && depth-- == 0)
            {
                var opening = snapshot.GetLine(snapshot.GetLineIndex(i));
                int indent = 0;
                while (opening.Start + indent < opening.End && snapshot[opening.Start + indent] is ' ' or '\t')
                {
                    indent++;
                }

                if (indent >= prefix)
                {
                    return null;
                }

                string text = string.Concat(snapshot.GetText(opening.Start, indent), close.ToString());
                return new CaretPart(line.Start, prefix, text, text.Length, text.Length);
            }
        }

        return null;
    }

    private bool Enter()
    {
        EditCarets(EditKind.Typing, keepAutoClosed: false, 0, static (model, selection, snapshot, _) => model.EnterPart(selection, snapshot));
        EnsureCaretVisible();
        return true;
    }

    // One caret's Enter: a line break with the line's indent; after an opening bracket one level more, and when its close
    // follows, the close on a line of its own at the line's indent.
    private CaretPart EnterPart(SelectionState selection, ITextSnapshot snapshot)
    {
        var line = snapshot.GetLine(snapshot.GetLineIndex(selection.Start));
        char[]? buffer = null;
        try
        {
            var text = TextReading.ReadLine(snapshot, line, ref buffer);
            int column = selection.Start - line.Start;
            var indent = text[..Math.Min(TextReading.IndentLength(text), column)];
            int before = column - 1;
            while (before >= 0 && text[before] is ' ' or '\t')
            {
                before--;
            }

            char? close = before >= 0 ? CloseOf(text[before]) : null;
            string newLine = NewLine;
            if (close is { } closing)
            {
                bool closeAfter = selection.End < snapshot.Length && snapshot[selection.End] == closing;
                string inner = string.Concat(newLine, indent, _indentUnit);
                string insert = closeAfter ? string.Concat(inner, newLine, indent) : inner;
                return new CaretPart(selection.Start, selection.Length, insert, inner.Length, inner.Length);
            }

            string plain = string.Concat(newLine, indent);
            return new CaretPart(selection.Start, selection.Length, plain, plain.Length, plain.Length);
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    private bool Backspace(bool control)
    {
        EditCarets(EditKind.Deleting, keepAutoClosed: true, control, static (model, selection, snapshot, word) => model.BackspacePart(selection, snapshot, word));
        EnsureCaretVisible();
        return true;
    }

    // One caret's Backspace: the selection; else the word (Ctrl), an auto-inserted pair the caret is inside, the spaces
    // back to the tab stop in leading white space, or the character before.
    private CaretPart BackspacePart(SelectionState selection, ITextSnapshot snapshot, bool control)
    {
        if (!selection.IsEmpty)
        {
            return CaretPart.Delete(selection.Start, selection.End);
        }

        int caret = selection.Caret;
        if (caret == 0)
        {
            return CaretPart.Stay(selection);
        }

        if (control)
        {
            return CaretPart.Delete(WordLeft(caret), caret);
        }

        if (caret < snapshot.Length && IsAutoClosed(caret) && OpenOf(snapshot[caret]) is { } open && snapshot[caret - 1] == open)
        {
            return CaretPart.Delete(caret - 1, caret + 1);
        }

        var line = snapshot.GetLine(snapshot.GetLineIndex(caret));
        int column = caret - line.Start;
        if (column > 0 && Options.InsertSpaces)
        {
            char[]? buffer = null;
            try
            {
                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                if (text[..column].IndexOfAnyExcept(' ') < 0)
                {
                    int stop = (column - 1) / Options.TabSize * Options.TabSize;
                    return CaretPart.Delete(line.Start + stop, caret);
                }
            }
            finally
            {
                TextReading.Return(buffer);
            }
        }

        return CaretPart.Delete(CharLeft(caret), caret);
    }

    private bool DeleteForward(bool control)
    {
        EditCarets(EditKind.Deleting, keepAutoClosed: true, control, static (model, selection, snapshot, word) => model.DeletePart(selection, snapshot, word));
        EnsureCaretVisible();
        return true;
    }

    private CaretPart DeletePart(SelectionState selection, ITextSnapshot snapshot, bool control)
    {
        if (!selection.IsEmpty)
        {
            return CaretPart.Delete(selection.Start, selection.End);
        }

        int caret = selection.Caret;
        if (caret >= snapshot.Length)
        {
            return CaretPart.Stay(selection);
        }

        if (control)
        {
            return CaretPart.Delete(caret, WordRight(caret));
        }

        var line = snapshot.GetLine(snapshot.GetLineIndex(caret));
        return CaretPart.Delete(caret, caret >= line.End ? line.EndIncludingBreak : CharRight(caret));
    }

    private (int First, int Last) SelectedLines() => SelectedLinesOf(_selection);

    private (int First, int Last) SelectedLinesOf(SelectionState selection)
    {
        var snapshot = Snapshot;
        int first = snapshot.GetLineIndex(selection.Start);
        int last = snapshot.GetLineIndex(selection.End);
        if (last > first && snapshot.GetLine(last).Start == selection.End)
        {
            last--;
        }

        return (first, last);
    }

    private bool Indent()
    {
        var snapshot = Snapshot;
        bool lines = false;
        foreach (var selection in Selections)
        {
            if (!selection.IsEmpty && snapshot.GetLineIndex(selection.Start) != snapshot.GetLineIndex(selection.End))
            {
                lines = true;
                break;
            }
        }

        if (lines)
        {
            return IndentSelectedLines(tab: true);
        }

        EditCarets(EditKind.Typing, keepAutoClosed: false, 0, static (model, selection, snapshot, _) => model.TabPart(selection));
        EnsureCaretVisible();
        return true;
    }

    // One caret's Tab on one line: the selection replaced by an indent (to the next tab stop with spaces).
    private CaretPart TabPart(SelectionState selection)
    {
        string insert = _indentUnit;
        if (Options.InsertSpaces)
        {
            int column = ColumnOf(selection.Start);
            int width = Options.TabSize - (column % Options.TabSize);
            insert = width == _indentUnit.Length ? _indentUnit : new string(' ', width);
        }

        return new CaretPart(selection.Start, selection.Length, insert, insert.Length, insert.Length);
    }

    private static int MapInserted(int position, ReadOnlySpan<TextEdit> inserts, bool atStartStays)
    {
        int delta = 0;
        foreach (var edit in inserts)
        {
            if (edit.Start < position || (edit.Start == position && !atStartStays))
            {
                delta += edit.Text.Length;
            }
        }

        return position + delta;
    }

    private static int MapDeleted(int position, ReadOnlySpan<TextEdit> deletes)
    {
        int delta = 0;
        foreach (var edit in deletes)
        {
            if (position >= edit.End)
            {
                delta += edit.Length;
            }
            else if (position > edit.Start)
            {
                return edit.Start - delta;
            }
        }

        return position - delta;
    }

    private bool Undo()
    {
        if (_readOnly)
        {
            RefusedEdits++;
            return true;
        }

        _applying = true;
        SelectionState? restored;
        try
        {
            restored = _document.Undo();
        }
        finally
        {
            _applying = false;
        }

        RestoreSelections(restored);
        return true;
    }

    // After an undo or a redo: every caret the step kept (a multi-selection document), else its one selection.
    private void RestoreSelections(SelectionState? restored)
    {
        if (_document is IMultiSelectionDocument multi && multi.RestoredSelections.Length > 1)
        {
            SetSelectionsCore(multi.RestoredSelections, multi.RestoredPrimary, keepDesiredColumns: false, keepAutoClosed: false, keepBox: false);
            EnsureCaretVisible();
        }
        else if (restored is { } selection)
        {
            SetSelection(selection);
            EnsureCaretVisible();
        }
    }

    private bool Redo()
    {
        if (_readOnly)
        {
            RefusedEdits++;
            return true;
        }

        _applying = true;
        SelectionState? restored;
        try
        {
            restored = _document.Redo();
        }
        finally
        {
            _applying = false;
        }

        RestoreSelections(restored);
        return true;
    }

    // ---- clipboard ----

    private string CopiedText(out int start, out int end)
    {
        var snapshot = Snapshot;
        var selection = _selection;
        if (!selection.IsEmpty)
        {
            start = selection.Start;
            end = selection.End;
        }
        else
        {
            var line = snapshot.GetLine(snapshot.GetLineIndex(selection.Caret));
            start = line.Start;
            end = line.EndIncludingBreak;
        }

        return snapshot.GetText(start, end - start);
    }

    private bool Copy()
    {
        if (CopyFromCard())
        {
            return true;
        }

        string text = HasMultipleCarets ? CopiedTexts() : CopiedText(out _, out _);
        if (text.Length > 0)
        {
            _ = SetClipboardAsync(text);
        }

        return true;
    }

    private bool Cut()
    {
        if (HasMultipleCarets)
        {
            string all = CopiedTexts();
            _ = SetClipboardAsync(all);
            EditCarets(EditKind.Command, keepAutoClosed: false, 0, static (model, selection, snapshot, _) =>
            {
                if (!selection.IsEmpty)
                {
                    return CaretPart.Delete(selection.Start, selection.End);
                }

                var line = snapshot.GetLine(snapshot.GetLineIndex(selection.Caret));
                return CaretPart.Delete(line.Start, line.EndIncludingBreak);
            });
            EnsureCaretVisible();
            return true;
        }

        var snapshot = Snapshot;
        string text = CopiedText(out int start, out int end);
        if (text.Length == 0)
        {
            return true;
        }

        _ = SetClipboardAsync(text);
        if (_selection.IsEmpty && end == snapshot.Length && start > 0 && start == snapshot.GetLine(snapshot.GetLineIndex(start)).Start)
        {
            // The last line, which has no break of its own: the previous line's break goes with it.
            start = snapshot.GetLine(snapshot.GetLineIndex(start) - 1).End;
        }

        Replace(start, end - start, "", EditKind.Command, SelectionState.At(start));
        EnsureCaretVisible();
        return true;
    }

    // Several carets: each selection's text (an empty one's whole line), one per line.
    private string CopiedTexts()
    {
        var snapshot = Snapshot;
        var text = new System.Text.StringBuilder();
        string newLine = NewLine;
        var all = Selections;
        for (int i = 0; i < all.Length; i++)
        {
            if (i > 0)
            {
                text.Append(newLine);
            }

            var selection = all[i];
            if (selection.IsEmpty)
            {
                var line = snapshot.GetLine(snapshot.GetLineIndex(selection.Caret));
                text.Append(snapshot.GetText(line.Start, line.Length));
            }
            else
            {
                text.Append(snapshot.GetText(selection.Start, selection.Length));
            }
        }

        return text.ToString();
    }

    private bool Paste()
    {
        _ = PasteAsync();
        return true;
    }

    private async Task SetClipboardAsync(string text)
    {
        try
        {
            await _clipboard.SetTextAsync(text);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            ClipboardFailed?.Invoke(this, new ClipboardFailedEventArgs(e));
        }
    }

    private async Task PasteAsync()
    {
        string? text;
        try
        {
            text = await _clipboard.GetTextAsync();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            ClipboardFailed?.Invoke(this, new ClipboardFailedEventArgs(e));
            return;
        }

        if (string.IsNullOrEmpty(text) || _disposed)
        {
            return;
        }

        text = text.ReplaceLineEndings(NewLine);
        PasteText(text);
    }

    /// <summary>
    /// Pastes <paramref name="text"/> at every caret (never auto-closing a bracket); with several carets and as many lines
    /// as carets, one line at each. One undo step.
    /// </summary>
    public void PasteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!HasMultipleCarets)
        {
            var selection = _selection;
            Replace(selection.Start, selection.Length, text, EditKind.Paste, SelectionState.At(selection.Start + text.Length));
            EnsureCaretVisible();
            return;
        }

        string newLine = NewLine;
        var lines = text.Split(newLine);
        if (lines.Length == _carets.Count + 1 && lines[^1].Length == 0)
        {
            lines = lines[..^1];
        }

        bool each = lines.Length == _carets.Count;
        int index = 0;
        EditCarets(EditKind.Paste, keepAutoClosed: false, (each ? lines : null, text), (model, selection, snapshot, state) =>
        {
            string insert = state.Item1 is { } perCaret ? perCaret[index++] : state.text;
            return new CaretPart(selection.Start, selection.Length, insert, insert.Length, insert.Length);
        });
        EnsureCaretVisible();
    }
}
