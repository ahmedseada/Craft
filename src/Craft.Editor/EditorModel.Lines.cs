// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

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

    /// <summary>Copies the selected lines above or below them (Shift+Alt+Up and Down; Visual Studio's Ctrl+D below); the selection goes with the copy below.</summary>
    public bool CopyLines(LineDirection direction)
    {
        var snapshot = Snapshot;
        var selection = _selection;
        var (first, last) = SelectedLines();
        var firstLine = snapshot.GetLine(first);
        var lastLine = snapshot.GetLine(last);
        string block = snapshot.GetText(firstLine.Start, lastLine.End - firstLine.Start);
        string newLine = NewLine;
        if (direction == LineDirection.Up)
        {
            // The copy goes above: the text the selection is on moves down, so the selection stays where it is (on the copy).
            Replace(firstLine.Start, 0, string.Concat(block, newLine), EditKind.Command, selection);
        }
        else
        {
            int shift = block.Length + newLine.Length;
            Replace(lastLine.End, 0, string.Concat(newLine, block), EditKind.Command, new SelectionState(selection.Anchor + shift, selection.Caret + shift));
        }

        EnsureCaretVisible();
        return true;
    }

    /// <summary>Duplicates the selected lines below them, the selection on the copy.</summary>
    public bool DuplicateLines() => CopyLines(LineDirection.Down);

    /// <summary>Deletes the selected lines with their line breaks (Ctrl+Shift+K; Visual Studio's Ctrl+Shift+L); the caret goes to the start of the line after.</summary>
    public bool DeleteLines()
    {
        var snapshot = Snapshot;
        var (first, last) = SelectedLines();
        var firstLine = snapshot.GetLine(first);
        var lastLine = snapshot.GetLine(last);
        int start = firstLine.Start;
        int end = lastLine.EndIncludingBreak;
        if (lastLine.BreakLength == 0 && first > 0)
        {
            start = snapshot.GetLine(first - 1).End;    // the last line: the break before it goes with it
        }

        if (end == start)
        {
            return true;
        }

        int caret = lastLine.BreakLength == 0 && first > 0 ? snapshot.GetLine(first - 1).Start : start;
        Replace(start, end - start, "", EditKind.Command, SelectionState.At(caret));
        EnsureCaretVisible();
        return true;
    }

    /// <summary>Moves the selected lines one line up or down (Alt+Up and Down), with the selection; at the first or last line nothing moves.</summary>
    public bool MoveLines(LineDirection direction)
    {
        var snapshot = Snapshot;
        var selection = _selection;
        var (first, last) = SelectedLines();
        bool up = direction == LineDirection.Up;
        if (up ? first == 0 : last >= snapshot.LineCount - 1)
        {
            return true;
        }

        var firstLine = snapshot.GetLine(first);
        var lastLine = snapshot.GetLine(last);
        var other = snapshot.GetLine(up ? first - 1 : last + 1);
        string block = snapshot.GetText(firstLine.Start, lastLine.End - firstLine.Start);
        string moved = snapshot.GetText(other.Start, other.Length);
        string newLine = NewLine;
        int start = up ? other.Start : firstLine.Start;
        int end = up ? lastLine.End : other.End;
        string text = up ? string.Concat(block, newLine, moved) : string.Concat(moved, newLine, block);
        int shift = up ? other.Start - firstLine.Start : moved.Length + newLine.Length;
        Replace(start, end - start, text, EditKind.Command, new SelectionState(selection.Anchor + shift, selection.Caret + shift));
        EnsureCaretVisible();
        return true;
    }

    /// <summary>Indents the selected lines by one level, whatever the selection (Ctrl+]).</summary>
    public bool IndentLines()
    {
        var selection = _selection;
        var snapshot = Snapshot;
        var (first, last) = SelectedLines();
        var edits = new List<TextEdit>(last - first + 1);
        for (int i = first; i <= last; i++)
        {
            var line = snapshot.GetLine(i);
            if (line.Length > 0 || first == last)
            {
                edits.Add(TextEdit.Insert(line.Start, _indentUnit));
            }
        }

        if (edits.Count == 0)
        {
            return true;
        }

        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edits);
        int anchor = MapInserted(selection.Anchor, span, atStartStays: false);
        int caret = MapInserted(selection.Caret, span, atStartStays: false);
        ApplyEdits(span, EditKind.Command, new SelectionState(anchor, caret));
        EnsureCaretVisible();
        return true;
    }

    /// <summary>Outdents the selected lines by one level (Ctrl+[; Shift+Tab).</summary>
    public bool OutdentLines() => Outdent();

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

    // null toggles: removed when every non-blank line has the comment.
    private bool Comment(bool? remove)
    {
        if (Rules.LineComment is not { Length: > 0 } prefix)
        {
            return false;
        }

        var snapshot = Snapshot;
        var selection = _selection;
        var (first, last) = SelectedLines();
        int indent = int.MaxValue;
        bool all = true;
        bool any = false;
        char[]? buffer = null;
        try
        {
            for (int i = first; i <= last; i++)
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

            if (!any && first != last)
            {
                return true;
            }

            bool removing = remove ?? (any && all);
            var edits = new List<TextEdit>(last - first + 1);
            for (int i = first; i <= last; i++)
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
                else if (lead < text.Length || first == last)
                {
                    edits.Add(TextEdit.Insert(line.Start + Math.Min(lead, indent == int.MaxValue ? lead : indent), prefix + " "));
                }
            }

            if (edits.Count == 0)
            {
                return true;
            }

            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edits);
            var after = removing
                ? new SelectionState(MapDeleted(selection.Anchor, span), MapDeleted(selection.Caret, span))
                : new SelectionState(MapInserted(selection.Anchor, span, atStartStays: selection.Anchor == selection.Start && !selection.IsEmpty), MapInserted(selection.Caret, span, atStartStays: selection.Caret == selection.Start && !selection.IsEmpty));
            ApplyEdits(span, EditKind.Command, after);
        }
        finally
        {
            TextReading.Return(buffer);
        }

        EnsureCaretVisible();
        return true;
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
