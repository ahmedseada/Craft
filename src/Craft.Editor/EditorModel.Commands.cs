// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Editor;

public sealed partial class EditorModel
{
    private readonly List<int> _autoClosed = [];
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
        if (!Completion.IsOpen && InlineCompletion.HandleKey(key, modifiers))
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
            EditorKey.Tab when !control => shift ? Outdent() : Indent(),
            EditorKey.Escape => Escape(),
            EditorKey.Insert when !shift && !control => ToggleOverwrite(),
            EditorKey.F3 => shift ? Find.FindPrevious() || true : Find.FindNext() || true,
            EditorKey.A when control => SelectAll(),
            EditorKey.C when control => Copy(),
            EditorKey.X when control => Cut(),
            EditorKey.V when control => Paste(),
            EditorKey.Z when control => shift ? Redo() : Undo(),
            EditorKey.Y when control => Redo(),
            EditorKey.S when control => Raise(SaveRequested),
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

        NoteInput();
        InlineCompletion.TypeThrough(text);
        var selection = _selection;
        var snapshot = Snapshot;
        if (text.Length == 1 && selection.IsEmpty && TryOvertypeClose(text[0]))
        {
            AfterTyped(text);
            return;
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

        string insert = text;
        bool closed = false;
        if (Options.AutoCloseBrackets && text.Length == 1 && selection.IsEmpty && CloseOf(text[0]) is { } close && AllowsAutoClose(start + length))
        {
            insert = string.Concat(text, close.ToString());
            closed = true;
        }

        ShiftAutoClosed(start, insert.Length - length);
        Replace(start, length, insert, EditKind.Typing, SelectionState.At(start + text.Length), keepAutoClosed: true);
        if (closed)
        {
            _autoClosed.Add(start + text.Length);
        }

        EnsureCaretVisible();
        AfterTyped(text);
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
        InlineCompletion.Dismiss();

        if (PopupRowAt(viewX, viewY) is { } row)
        {
            _pointerDown = false;
            Completion.Click(row, _clickCount);
            return;
        }

        CloseTooltips();
        _completion?.Close();
        int position = PositionFromView(viewX, viewY);
        if ((modifiers & KeyModifiers.Shift) != 0 && _clickCount == 1)
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
        if (!_pointerDown || _clickCount != 1)
        {
            return;
        }

        NoteInput();
        SetSelection(new SelectionState(_selection.Anchor, PositionFromView(viewX, viewY)));
        EnsureCaretVisible();
    }

    /// <summary>The primary button was released.</summary>
    public void PointerReleased() => _pointerDown = false;

    private int PositionFromView(float viewX, float viewY)
    {
        float x = viewX < TextLeft ? TextLeft : viewX + ScrollX;
        return PositionFromPoint(x, viewY + ScrollY);
    }

    // ---- movement ----

    private bool Move(EditorKey key, bool shift, bool control)
    {
        var selection = _selection;
        var snapshot = Snapshot;
        int caret = selection.Caret;
        bool vertical = false;
        int target;
        switch (key)
        {
            case EditorKey.Left:
                target = !shift && !selection.IsEmpty ? selection.Start : control ? WordLeft(caret) : CharLeft(caret);
                break;
            case EditorKey.Right:
                target = !shift && !selection.IsEmpty ? selection.End : control ? WordRight(caret) : CharRight(caret);
                break;
            case EditorKey.Up:
            case EditorKey.Down:
                target = LineMove(caret, key == EditorKey.Up ? -1 : 1);
                vertical = true;
                break;
            case EditorKey.PageUp:
            case EditorKey.PageDown:
                int lines = Math.Max(1, (int)MathF.Floor(ViewportHeight / Metrics.LineHeight));
                int sign = key == EditorKey.PageUp ? -1 : 1;
                target = LineMove(caret, sign * lines);
                vertical = true;
                if (ViewportHeight > 0)
                {
                    float y = Math.Clamp(ScrollY + (sign * lines * Metrics.LineHeight), 0, Math.Max(0, ContentHeight - ViewportHeight));
                    if (y != ScrollY)
                    {
                        ScrollTo(ScrollX, y);
                    }
                }

                break;
            case EditorKey.Home:
                target = control ? 0 : SmartHome(caret);
                break;
            default:
                target = control ? snapshot.Length : snapshot.GetLine(snapshot.GetLineIndex(caret)).End;
                break;
        }

        SetSelection(shift ? new SelectionState(selection.Anchor, target) : SelectionState.At(target), keepDesiredColumn: vertical);
        EnsureCaretVisible();
        return true;
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

    private int LineMove(int position, int lines)
    {
        var snapshot = Snapshot;
        int index = snapshot.GetLineIndex(position);
        if (_desiredColumn < 0)
        {
            _desiredColumn = ColumnOf(position);
        }

        int target = index + lines;
        if (target < 0)
        {
            return 0;
        }

        if (target >= snapshot.LineCount)
        {
            return snapshot.Length;
        }

        return PositionAtColumn(snapshot.GetLine(target), _desiredColumn, nearest: false);
    }

    private bool SelectAll()
    {
        SetSelection(new SelectionState(0, Snapshot.Length));
        return true;
    }

    private bool Escape()
    {
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

    internal void ApplyEdits(ReadOnlySpan<TextEdit> edits, EditKind kind, SelectionState after, bool keepAutoClosed = false)
    {
        var before = _selection;
        _applying = true;
        _applyingAfter = after;
        try
        {
            _document.Edit(edits, kind, before, after);
        }
        finally
        {
            _applying = false;
            _applyingAfter = null;
        }

        SetSelection(after, keepAutoClosed: keepAutoClosed);
    }

    private string NewLine
    {
        get
        {
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

    private bool TryOvertypeClose(char c)
    {
        int caret = _selection.Caret;
        if (caret >= Snapshot.Length || Snapshot[caret] != c || OpenOf(c) is null || !_autoClosed.Contains(caret))
        {
            return false;
        }

        _autoClosed.Remove(caret);
        SetSelection(SelectionState.At(caret + 1), keepAutoClosed: true);
        EnsureCaretVisible();
        return true;
    }

    private void ShiftAutoClosed(int at, int delta)
    {
        for (int i = 0; i < _autoClosed.Count; i++)
        {
            if (_autoClosed[i] >= at)
            {
                _autoClosed[i] += delta;
            }
        }
    }

    private bool Enter()
    {
        var snapshot = Snapshot;
        var selection = _selection;
        var line = snapshot.GetLine(snapshot.GetLineIndex(selection.Start));
        char[]? buffer = null;
        string insert;
        int caretOffset;
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
                caretOffset = inner.Length;
                insert = closeAfter ? string.Concat(inner, newLine, indent) : inner;
            }
            else
            {
                insert = string.Concat(newLine, indent);
                caretOffset = insert.Length;
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }

        Replace(selection.Start, selection.Length, insert, EditKind.Typing, SelectionState.At(selection.Start + caretOffset));
        EnsureCaretVisible();
        return true;
    }

    private bool Backspace(bool control)
    {
        var selection = _selection;
        if (!selection.IsEmpty)
        {
            return DeleteRange(selection.Start, selection.End);
        }

        int caret = selection.Caret;
        if (caret == 0)
        {
            return true;
        }

        if (control)
        {
            return DeleteRange(WordLeft(caret), caret);
        }

        var snapshot = Snapshot;
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
                    return DeleteRange(line.Start + stop, caret);
                }
            }
            finally
            {
                TextReading.Return(buffer);
            }
        }

        return DeleteRange(CharLeft(caret), caret);
    }

    private bool DeleteForward(bool control)
    {
        var selection = _selection;
        if (!selection.IsEmpty)
        {
            return DeleteRange(selection.Start, selection.End);
        }

        int caret = selection.Caret;
        if (caret >= Snapshot.Length)
        {
            return true;
        }

        if (control)
        {
            return DeleteRange(caret, WordRight(caret));
        }

        var snapshot = Snapshot;
        var line = snapshot.GetLine(snapshot.GetLineIndex(caret));
        return DeleteRange(caret, caret >= line.End ? line.EndIncludingBreak : CharRight(caret));
    }

    private bool DeleteRange(int start, int end)
    {
        if (end > start)
        {
            Replace(start, end - start, "", EditKind.Deleting, SelectionState.At(start));
        }

        EnsureCaretVisible();
        return true;
    }

    private (int First, int Last) SelectedLines()
    {
        var snapshot = Snapshot;
        var selection = _selection;
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
        var selection = _selection;
        if (selection.IsEmpty || snapshot.GetLineIndex(selection.Start) == snapshot.GetLineIndex(selection.End))
        {
            string insert = _indentUnit;
            if (Options.InsertSpaces)
            {
                int column = ColumnOf(selection.Start);
                insert = new string(' ', Options.TabSize - (column % Options.TabSize));
            }

            Replace(selection.Start, selection.Length, insert, EditKind.Typing, SelectionState.At(selection.Start + insert.Length));
            EnsureCaretVisible();
            return true;
        }

        var (first, last) = SelectedLines();
        var edits = ArrayPool<TextEdit>.Shared.Rent(last - first + 1);
        try
        {
            int count = 0;
            for (int i = first; i <= last; i++)
            {
                var line = snapshot.GetLine(i);
                if (line.Length > 0)
                {
                    edits[count++] = TextEdit.Insert(line.Start, _indentUnit);
                }
            }

            var span = edits.AsSpan(0, count);
            bool anchorIsStart = selection.Anchor <= selection.Caret;
            int start = MapInserted(selection.Start, span, atStartStays: true);
            int end = MapInserted(selection.End, span, atStartStays: false);
            if (count > 0)
            {
                ApplyEdits(span, EditKind.Command, anchorIsStart ? new SelectionState(start, end) : new SelectionState(end, start));
            }
        }
        finally
        {
            ArrayPool<TextEdit>.Shared.Return(edits, clearArray: true);
        }

        EnsureCaretVisible();
        return true;
    }

    private bool Outdent()
    {
        var snapshot = Snapshot;
        var selection = _selection;
        var (first, last) = SelectedLines();
        var edits = ArrayPool<TextEdit>.Shared.Rent(last - first + 1);
        char[]? buffer = null;
        try
        {
            int count = 0;
            for (int i = first; i <= last; i++)
            {
                var line = snapshot.GetLine(i);
                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                int remove = 0;
                if (text.Length > 0 && text[0] == '\t')
                {
                    remove = 1;
                }
                else
                {
                    int spaces = 0;
                    while (spaces < text.Length && text[spaces] == ' ')
                    {
                        spaces++;
                    }

                    if (spaces > 0)
                    {
                        remove = Math.Min(spaces, spaces % Options.TabSize == 0 ? Options.TabSize : spaces % Options.TabSize);
                    }
                }

                if (remove > 0)
                {
                    edits[count++] = TextEdit.Delete(line.Start, remove);
                }
            }

            var span = edits.AsSpan(0, count);
            if (count > 0)
            {
                var after = new SelectionState(MapDeleted(selection.Anchor, span), MapDeleted(selection.Caret, span));
                ApplyEdits(span, EditKind.Command, after);
            }
        }
        finally
        {
            TextReading.Return(buffer);
            ArrayPool<TextEdit>.Shared.Return(edits, clearArray: true);
        }

        EnsureCaretVisible();
        return true;
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

        if (restored is { } selection)
        {
            SetSelection(selection);
            EnsureCaretVisible();
        }

        return true;
    }

    private bool Redo()
    {
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

        if (restored is { } selection)
        {
            SetSelection(selection);
            EnsureCaretVisible();
        }

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
        string text = CopiedText(out _, out _);
        if (text.Length > 0)
        {
            _ = SetClipboardAsync(text);
        }

        return true;
    }

    private bool Cut()
    {
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
        var selection = _selection;
        Replace(selection.Start, selection.Length, text, EditKind.Paste, SelectionState.At(selection.Start + text.Length));
        EnsureCaretVisible();
    }
}
