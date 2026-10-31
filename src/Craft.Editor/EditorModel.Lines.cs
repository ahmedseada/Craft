// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>Which way a line command goes.</summary>
public enum LineDirection
{
    /// <summary>Toward the first line.</summary>
    Up,

    /// <summary>Toward the last line.</summary>
    Down,
}

// The owner's note 16: the line commands a C# developer expects from Visual Studio and VS Code, each one undo step
// (EditKind.Command), on the lines the selection touches (the caret's line when it is empty). They run when the user
// asks (a key, the palette, a menu), never per keystroke typed, so they read the lines they touch and no more.
public sealed partial class EditorModel
{
    private readonly Stack<SelectionState> _expanded = new();
    private SelectionState? _expandedTo;

    /// <summary>
    /// Comments the selected lines with the language's line comment (<see cref="LanguageRules.LineComment"/>, "//"), or
    /// takes it off when every non-blank one has it (VS Code's Ctrl+/); false when the language has no line comment.
    /// </summary>
    public bool ToggleLineComment() => Comment(remove: null);

    /// <summary>Comments the selected lines (Visual Studio's Ctrl+K Ctrl+C); false when the language has no line comment.</summary>
    public bool CommentLines() => Comment(remove: false);

    /// <summary>Takes the line comment off the selected lines that have one (Ctrl+K Ctrl+U); false when the language has no line comment.</summary>
    public bool UncommentLines() => Comment(remove: true);

    /// <summary>Copies the selected lines above or below them (Shift+Alt+Up and Down; Visual Studio's Ctrl+D below); the selection goes with the copy below. Every caret's lines.</summary>
    public bool CopyLines(LineDirection direction)
    {
        var snapshot = Snapshot;
        var blocks = LineBlocks(out int count);
        var edits = ArrayPool<TextEdit>.Shared.Rent(count);
        var shifts = ArrayPool<int>.Shared.Rent(count);
        try
        {
            string newLine = NewLine;
            for (int b = 0; b < count; b++)
            {
                var firstLine = snapshot.GetLine(blocks[b].First);
                var lastLine = snapshot.GetLine(blocks[b].Last);
                string block = snapshot.GetText(firstLine.Start, lastLine.End - firstLine.Start);
                if (direction == LineDirection.Up)
                {
                    // The copy goes above: the text the selection is on moves down, so the selection stays where it is (on the copy).
                    edits[b] = TextEdit.Insert(firstLine.Start, string.Concat(block, newLine));
                    shifts[b] = 0;
                }
                else
                {
                    edits[b] = TextEdit.Insert(lastLine.End, string.Concat(newLine, block));
                    shifts[b] = block.Length + newLine.Length;
                }
            }

            ApplyBlockEdits(blocks, count, edits.AsSpan(0, count), shifts);
        }
        finally
        {
            ReturnBlocks(blocks);
            ArrayPool<TextEdit>.Shared.Return(edits, clearArray: true);
            ArrayPool<int>.Shared.Return(shifts);
        }

        EnsureCaretVisible();
        return true;
    }

    /// <summary>Duplicates the selected lines below them, the selection on the copy.</summary>
    public bool DuplicateLines() => CopyLines(LineDirection.Down);

    /// <summary>Deletes the selected lines with their line breaks (Ctrl+Shift+K; Visual Studio's Ctrl+Shift+L); the caret goes to the start of the line after. Every caret's lines.</summary>
    public bool DeleteLines()
    {
        var snapshot = Snapshot;
        var blocks = LineBlocks(out int count);
        var edits = ArrayPool<TextEdit>.Shared.Rent(count);
        var after = ArrayPool<SelectionState>.Shared.Rent(count);
        try
        {
            int edited = 0;
            int delta = 0;
            for (int b = 0; b < count; b++)
            {
                var (first, last) = blocks[b];
                var firstLine = snapshot.GetLine(first);
                var lastLine = snapshot.GetLine(last);
                int start = firstLine.Start;
                int end = lastLine.EndIncludingBreak;
                bool lastOfText = lastLine.BreakLength == 0 && first > 0;
                if (lastOfText)
                {
                    start = snapshot.GetLine(first - 1).End;    // the last line: the break before it goes with it
                }

                int caret = lastOfText ? snapshot.GetLine(first - 1).Start : start;
                after[b] = SelectionState.At(caret + delta);
                if (end > start)
                {
                    edits[edited++] = TextEdit.Delete(start, end - start);
                    delta -= end - start;
                }
            }

            if (edited > 0)
            {
                ApplyEdits(edits.AsSpan(0, edited), EditKind.Command, after.AsSpan(0, count), Math.Min(BlockOf(blocks, count, PrimarySelectionIndex), count - 1));
            }
        }
        finally
        {
            ReturnBlocks(blocks);
            ArrayPool<TextEdit>.Shared.Return(edits, clearArray: true);
            ArrayPool<SelectionState>.Shared.Return(after);
        }

        EnsureCaretVisible();
        return true;
    }

    /// <summary>Moves the selected lines one line up or down (Alt+Up and Down), with the selection; at the first or last line nothing moves. Every caret's lines.</summary>
    public bool MoveLines(LineDirection direction)
    {
        var snapshot = Snapshot;
        bool up = direction == LineDirection.Up;
        var blocks = LineBlocks(out int count);
        var edits = ArrayPool<TextEdit>.Shared.Rent(count);
        var shifts = ArrayPool<int>.Shared.Rent(count);
        try
        {
            if (up ? blocks[0].First == 0 : blocks[count - 1].Last >= snapshot.LineCount - 1)
            {
                return true;
            }

            string newLine = NewLine;
            for (int b = 0; b < count; b++)
            {
                var (first, last) = blocks[b];
                var firstLine = snapshot.GetLine(first);
                var lastLine = snapshot.GetLine(last);
                var other = snapshot.GetLine(up ? first - 1 : last + 1);
                string block = snapshot.GetText(firstLine.Start, lastLine.End - firstLine.Start);
                string moved = snapshot.GetText(other.Start, other.Length);
                int start = up ? other.Start : firstLine.Start;
                int end = up ? lastLine.End : other.End;
                string text = up ? string.Concat(block, newLine, moved) : string.Concat(moved, newLine, block);
                edits[b] = new TextEdit(start, end - start, text);
                shifts[b] = up ? other.Start - firstLine.Start : moved.Length + newLine.Length;
            }

            ApplyBlockEdits(blocks, count, edits.AsSpan(0, count), shifts);
        }
        finally
        {
            ReturnBlocks(blocks);
            ArrayPool<TextEdit>.Shared.Return(edits, clearArray: true);
            ArrayPool<int>.Shared.Return(shifts);
        }

        EnsureCaretVisible();
        return true;
    }

    /// <summary>Indents the selected lines by one level, whatever the selection (Ctrl+]). Every caret's lines.</summary>
    public bool IndentLines() => IndentSelectedLines(tab: false);

    /// <summary>Outdents the selected lines by one level (Ctrl+[; Shift+Tab). Every caret's lines.</summary>
    public bool OutdentLines()
    {
        var snapshot = Snapshot;
        var blocks = LineBlocks(out int count);
        var edits = new List<TextEdit>();
        char[]? buffer = null;
        try
        {
            for (int b = 0; b < count; b++)
            {
                for (int i = blocks[b].First; i <= blocks[b].Last; i++)
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
                        edits.Add(TextEdit.Delete(line.Start, remove));
                    }
                }
            }
        }
        finally
        {
            TextReading.Return(buffer);
            ReturnBlocks(blocks);
        }

        if (edits.Count > 0)
        {
            ApplyMapped(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edits), deleting: true, static (_, _) => false);
        }

        EnsureCaretVisible();
        return true;
    }

    // Indent every line of every caret's lines: Ctrl+] (empty lines too when a selection is on one line) or Tab over
    // several lines (non-empty lines only; a selection's start stays before the new indent).
    private bool IndentSelectedLines(bool tab)
    {
        var snapshot = Snapshot;
        var edits = new List<TextEdit>();
        foreach (var selection in Selections)
        {
            var (first, last) = SelectedLinesOf(selection);
            for (int i = first; i <= last; i++)
            {
                var line = snapshot.GetLine(i);
                if ((line.Length > 0 || (!tab && first == last)) && (edits.Count == 0 || edits[^1].Start < line.Start))
                {
                    edits.Add(TextEdit.Insert(line.Start, _indentUnit));
                }
            }
        }

        if (edits.Count > 0)
        {
            ApplyMapped(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edits), deleting: false,
                tab ? static (selection, position) => position == selection.Start : static (_, _) => false);
        }

        EnsureCaretVisible();
        return true;
    }

    /// <summary>
    /// Selects the caret's line with its break (Ctrl+L); when the selection already holds whole lines, the next line is
    /// added to it.
    /// </summary>
    public bool SelectLine()
    {
        var snapshot = Snapshot;
        var selection = _selection;
        var (first, last) = SelectedLines();
        var firstLine = snapshot.GetLine(first);
        var lastLine = snapshot.GetLine(last);
        bool whole = !selection.IsEmpty && selection.Start == firstLine.Start && selection.End == lastLine.EndIncludingBreak;
        int end = whole && last + 1 < snapshot.LineCount ? snapshot.GetLine(last + 1).EndIncludingBreak : lastLine.EndIncludingBreak;
        SetSelection(new SelectionState(firstLine.Start, end));
        EnsureCaretVisible();
        return true;
    }

    /// <summary>
    /// Grows the selection one step (Shift+Alt+Right): the word at the caret, then the inside of the brackets around it,
    /// then the brackets too, then its whole lines, then everything. <see cref="ShrinkSelection"/> walks back.
    /// </summary>
    public bool ExpandSelection()
    {
        var selection = _selection;
        if (_expandedTo != selection)
        {
            _expanded.Clear();
        }

        var next = NextExpansion(selection);
        if (next == selection)
        {
            return true;
        }

        _expanded.Push(selection);
        _expandedTo = next;
        SetSelection(next);
        EnsureCaretVisible();
        return true;
    }

    /// <summary>Takes the last <see cref="ExpandSelection"/> back (Shift+Alt+Left); after any other change of the selection it does nothing.</summary>
    public bool ShrinkSelection()
    {
        if (_expandedTo != _selection || _expanded.Count == 0)
        {
            _expanded.Clear();
            return true;
        }

        var back = _expanded.Pop();
        SetSelection(back);
        _expandedTo = _expanded.Count > 0 ? back : null;
        EnsureCaretVisible();
        return true;
    }

    /// <summary>Puts the caret at <paramref name="line"/> and <paramref name="column"/> (both from 1, kept within the text) and scrolls it into view (Ctrl+G).</summary>
    public bool GoToLine(int line, int column = 1)
    {
        var snapshot = Snapshot;
        var target = snapshot.GetLine(Math.Clamp(line, 1, snapshot.LineCount) - 1);
        int position = target.Start + Math.Clamp(column - 1, 0, target.Length);
        SetSelection(SelectionState.At(position));
        EnsureCaretVisible();
        return true;
    }

    // null toggles: removed when every non-blank line has the comment. Every caret's lines, decided together.
    private bool Comment(bool? remove)
    {
        if (Rules.LineComment is not { Length: > 0 } prefix)
        {
            return false;
        }

        var snapshot = Snapshot;
        var blocks = LineBlocks(out int count);
        int indent = int.MaxValue;
        bool all = true;
        bool any = false;
        bool single = count == 1 && blocks[0].First == blocks[0].Last;
        char[]? buffer = null;
        var edits = new List<TextEdit>();
        bool removing;
        try
        {
            for (int b = 0; b < count; b++)
            {
                for (int i = blocks[b].First; i <= blocks[b].Last; i++)
                {
                    var text = TextReading.ReadLine(snapshot, snapshot.GetLine(i), ref buffer);
                    int lead = TextReading.IndentLength(text);
                    if (lead == text.Length)
                    {
                        continue;     // blank lines are left as they are
                    }

                    any = true;
                    indent = Math.Min(indent, lead);
                    if (!text[lead..].StartsWith(prefix, StringComparison.Ordinal))
                    {
                        all = false;
                    }
                }
            }

            if (!any && !single)
            {
                return true;
            }

            removing = remove ?? (any && all);
            string inserted = prefix + " ";
            for (int b = 0; b < count; b++)
            {
                for (int i = blocks[b].First; i <= blocks[b].Last; i++)
                {
                    var line = snapshot.GetLine(i);
                    var text = TextReading.ReadLine(snapshot, line, ref buffer);
                    int lead = TextReading.IndentLength(text);
                    if (removing)
                    {
                        if (lead < text.Length && text[lead..].StartsWith(prefix, StringComparison.Ordinal))
                        {
                            int length = prefix.Length + (lead + prefix.Length < text.Length && text[lead + prefix.Length] == ' ' ? 1 : 0);
                            edits.Add(TextEdit.Delete(line.Start + lead, length));
                        }
                    }
                    else if (lead < text.Length || blocks[b].First == blocks[b].Last)
                    {
                        edits.Add(TextEdit.Insert(line.Start + Math.Min(lead, indent == int.MaxValue ? lead : indent), inserted));
                    }
                }
            }
        }
        finally
        {
            TextReading.Return(buffer);
            ReturnBlocks(blocks);
        }

        if (edits.Count == 0)
        {
            return true;
        }

        ApplyMapped(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edits), removing,
            static (selection, position) => position == selection.Start && !selection.IsEmpty);
        EnsureCaretVisible();
        return true;
    }

    // Applies inserts (or deletes) at line starts as one Command step, every selection mapped through them; a position
    // `stays(selection, position)` says stays before an insert at it.
    private void ApplyMapped(ReadOnlySpan<TextEdit> edits, bool deleting, Func<SelectionState, int, bool> stays)
    {
        var prefix = ArrayPool<int>.Shared.Rent(edits.Length + 1);
        var all = Selections;
        var after = ArrayPool<SelectionState>.Shared.Rent(all.Length);
        try
        {
            prefix[0] = 0;
            for (int i = 0; i < edits.Length; i++)
            {
                prefix[i + 1] = prefix[i] + (deleting ? edits[i].Length : edits[i].Text.Length);
            }

            var sums = prefix.AsSpan(0, edits.Length + 1);
            for (int i = 0; i < all.Length; i++)
            {
                var s = all[i];
                after[i] = deleting
                    ? new SelectionState(MapDeletedSorted(s.Anchor, edits, sums), MapDeletedSorted(s.Caret, edits, sums))
                    : new SelectionState(MapInsertedSorted(s.Anchor, edits, sums, stays(s, s.Anchor)), MapInsertedSorted(s.Caret, edits, sums, stays(s, s.Caret)));
            }

            ApplyEdits(edits, EditKind.Command, after.AsSpan(0, all.Length), PrimarySelectionIndex);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(prefix);
            ArrayPool<SelectionState>.Shared.Return(after);
        }
    }

    private static int MapInsertedSorted(int position, ReadOnlySpan<TextEdit> inserts, ReadOnlySpan<int> prefix, bool atStartStays)
    {
        int lo = 0, hi = inserts.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (inserts[mid].Start < position || (!atStartStays && inserts[mid].Start == position))
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return position + prefix[lo];
    }

    private static int MapDeletedSorted(int position, ReadOnlySpan<TextEdit> deletes, ReadOnlySpan<int> prefix)
    {
        int lo = 0, hi = deletes.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (deletes[mid].Start < position)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        if (lo > 0 && position < deletes[lo - 1].End)
        {
            return deletes[lo - 1].Start - prefix[lo - 1];
        }

        return position - prefix[lo];
    }

    // The lines of every selection, merged where they overlap or touch, in order (rented: ReturnBlocks).
    private (int First, int Last)[] LineBlocks(out int count)
    {
        var all = Selections;
        var blocks = ArrayPool<(int First, int Last)>.Shared.Rent(all.Length);
        count = 0;
        foreach (var selection in all)
        {
            var (first, last) = SelectedLinesOf(selection);
            if (count > 0 && first <= blocks[count - 1].Last + 1)
            {
                blocks[count - 1].Last = Math.Max(blocks[count - 1].Last, last);
            }
            else
            {
                blocks[count++] = (first, last);
            }
        }

        return blocks;
    }

    private static void ReturnBlocks((int First, int Last)[] blocks) => ArrayPool<(int First, int Last)>.Shared.Return(blocks);

    // The block holding selection `index`.
    private int BlockOf((int First, int Last)[] blocks, int count, int index)
    {
        var (first, _) = SelectedLinesOf(Selections[index]);
        for (int b = 0; b < count; b++)
        {
            if (first <= blocks[b].Last)
            {
                return b;
            }
        }

        return count - 1;
    }

    // One edit per block, as one Command step: each selection moves with the edits before its block plus its block's shift.
    private void ApplyBlockEdits((int First, int Last)[] blocks, int count, ReadOnlySpan<TextEdit> edits, int[] shifts)
    {
        var all = Selections;
        var after = ArrayPool<SelectionState>.Shared.Rent(all.Length);
        try
        {
            int b = 0;
            int delta = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var s = all[i];
                var (first, _) = SelectedLinesOf(s);
                while (b < count - 1 && first > blocks[b].Last)
                {
                    delta += edits[b].Delta;
                    b++;
                }

                int move = delta + shifts[b];
                after[i] = new SelectionState(s.Anchor + move, s.Caret + move);
            }

            ApplyEdits(edits, EditKind.Command, after.AsSpan(0, all.Length), PrimarySelectionIndex);
        }
        finally
        {
            ArrayPool<SelectionState>.Shared.Return(after);
        }
    }

    private SelectionState NextExpansion(SelectionState selection)
    {
        var snapshot = Snapshot;
        if (selection.IsEmpty)
        {
            var (start, end) = WordAt(selection.Caret);
            if (end > start)
            {
                return new SelectionState(start, end);
            }
        }

        if (EnclosingBrackets(selection.Start, selection.End) is { } brackets)
        {
            var inside = new SelectionState(brackets.Open + 1, brackets.Close);
            if (inside.Start < selection.Start || inside.End > selection.End)
            {
                return inside;
            }

            return new SelectionState(brackets.Open, brackets.Close + 1);
        }

        var first = snapshot.GetLine(snapshot.GetLineIndex(selection.Start));
        var last = snapshot.GetLine(snapshot.GetLineIndex(selection.End));
        var lines = new SelectionState(first.Start, last.EndIncludingBreak);
        if (lines.Start < selection.Start || lines.End > selection.End)
        {
            return lines;
        }

        return new SelectionState(0, snapshot.Length);
    }

    // The nearest bracket pair of the language around [start, end) (strings and comments are not told apart: a cheap
    // step), scanning at most BracketScanLines lines each way; null when there is none.
    private (int Open, int Close)? EnclosingBrackets(int start, int end)
    {
        var snapshot = Snapshot;
        var brackets = Rules.Brackets;
        if (brackets.Count == 0)
        {
            return null;
        }

        int firstLine = Math.Max(0, snapshot.GetLineIndex(start) - Options.BracketScanLines);
        int lastLine = Math.Min(snapshot.LineCount - 1, snapshot.GetLineIndex(end) + Options.BracketScanLines);
        int lower = snapshot.GetLine(firstLine).Start;
        int upper = snapshot.GetLine(lastLine).EndIncludingBreak;
        int searchFrom = start;
        while (true)
        {
            int open = -1;
            char close = '\0';
            int depth = 0;
            for (int i = searchFrom - 1; i >= lower; i--)
            {
                char c = snapshot[i];
                if (IsClose(c, brackets))
                {
                    depth++;
                }
                else if (CloseOf(c) is { } closing)
                {
                    if (depth == 0)
                    {
                        open = i;
                        close = closing;
                        break;
                    }

                    depth--;
                }
            }

            if (open < 0)
            {
                return null;
            }

            depth = 0;
            for (int i = open + 1; i < upper; i++)
            {
                char c = snapshot[i];
                if (CloseOf(c) is not null)
                {
                    depth++;
                }
                else if (IsClose(c, brackets))
                {
                    if (depth > 0)
                    {
                        depth--;
                        continue;
                    }

                    if (c == close && i >= end)
                    {
                        return (open, i);
                    }

                    break;    // a mismatched or too-near close: try the bracket before
                }
            }

            searchFrom = open;
        }

        static bool IsClose(char c, IReadOnlyList<BracketPair> pairs)
        {
            foreach (var pair in pairs)
            {
                if (pair.Close == c)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
