// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Globalization;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>Which carets an accepted completion or ghost text also goes to (the X restart, piece 2b).</summary>
internal enum CaretMatch
{
    /// <summary>A completion item: the caret's text around it equals the text the item replaces at the primary (a word start stays a word start).</summary>
    ReplacedText,

    /// <summary>Ghost text: the caret's line before it equals the primary's, its leading white space aside (its other lines take the caret's indent).</summary>
    LinePrefix,
}

// The X restart, piece 2b: completion and ghost text at several carets. One request is made at the primary caret; what is
// accepted goes to every caret whose text matches the primary's, the rest are left where they are and the status says so.
// All of it is one change and one undo step through the multi-caret edit path (CommitEdits).
public sealed partial class EditorModel
{
    /// <summary>The last message the editor asked the status bar to show (a completion applied at some carets only), or null.</summary>
    public string? LastStatusMessage { get; private set; }

    /// <summary>Raised with a short message for the status bar (a completion applied at 3 of 4 carets).</summary>
    public event EventHandler<string>? StatusRequested;

    /// <summary>Whether the completion list and ghost text work now: one caret, or several with <c>CRAFT_COMPLETION_ALL_CARETS</c> on.</summary>
    internal bool CompletesHere => !HasMultipleCarets || Options.CompletionAllCarets;

    /// <summary>
    /// How many carets other than the primary <paramref name="match"/> takes for a replacement of <paramref name="before"/>
    /// characters before the primary caret and <paramref name="after"/> after it (the "+N" of ghost text).
    /// </summary>
    internal int OtherMatchingCarets(CaretMatch match, int before, int after)
    {
        if (!HasMultipleCarets)
        {
            return 0;
        }

        var selections = Selections;
        int primary = PrimarySelectionIndex;
        var snapshot = Snapshot;
        var reference = Reference(snapshot, selections[primary].Caret, match, before, after);
        if (!reference.Valid)
        {
            return 0;
        }

        int count = 0;
        int lastEnd = -1;
        int primaryCaret = selections[primary].Caret;
        for (int i = 0; i < selections.Length; i++)
        {
            if (i == primary || Matches(snapshot, selections[i], reference, match, before, after, lastEnd, primaryCaret))
            {
                count += i == primary ? 0 : 1;
                lastEnd = selections[i].Caret + after;
            }
        }

        return count;
    }

    /// <summary>
    /// Replaces <paramref name="before"/> characters before every matching caret and <paramref name="after"/> after it with
    /// <paramref name="text"/> (ghost text: with its later lines in the caret's indent), the caret then at
    /// <paramref name="caretOffset"/> into the new text; the primary always takes it, the carets that do not match stay where
    /// they are. One change, one undo step. Returns how many carets took it; when some were left, the status says so.
    /// </summary>
    internal int ApplyAtMatchingCarets(CaretMatch match, int before, int after, string text, int caretOffset, string what)
    {
        var selections = Selections;
        int count = selections.Length;
        int primary = PrimarySelectionIndex;
        var snapshot = Snapshot;
        var reference = Reference(snapshot, selections[primary].Caret, match, before, after);
        var edits = ArrayPool<TextEdit>.Shared.Rent(count);
        var afterEdit = ArrayPool<SelectionState>.Shared.Rent(count);
        int taken = 0;
        int primaryCaret = selections[primary].Caret;
        try
        {
            int edited = 0;
            int delta = 0;
            int lastEnd = -1;
            for (int i = 0; i < count; i++)
            {
                var selection = selections[i];
                bool take = i == primary || (reference.Valid && Matches(snapshot, selection, reference, match, before, after, lastEnd, primaryCaret));
                if (!take)
                {
                    afterEdit[i] = new SelectionState(selection.Anchor + delta, selection.Caret + delta);
                    continue;
                }

                int start = selection.Caret - before;
                string inserted = match == CaretMatch.LinePrefix && i != primary ? Reindent(text, reference.Indent, IndentOf(snapshot, selection.Caret)) : text;
                int offset = inserted.Length == text.Length ? caretOffset : caretOffset + (inserted.Length - text.Length);
                edits[edited++] = new TextEdit(start, before + after, inserted);
                int at = start + delta + Math.Clamp(offset, 0, inserted.Length);
                afterEdit[i] = SelectionState.At(at);
                delta += inserted.Length - (before + after);
                lastEnd = selection.Caret + after;
                taken++;
            }

            ApplyEdits(edits.AsSpan(0, edited), EditKind.Command, afterEdit.AsSpan(0, count), primary);
        }
        finally
        {
            ArrayPool<TextEdit>.Shared.Return(edits, clearArray: true);
            ArrayPool<SelectionState>.Shared.Return(afterEdit);
        }

        EnsureCaretVisible();
        if (taken < count)
        {
            Status(string.Create(CultureInfo.InvariantCulture, $"{what} applied at {taken} of {count} carets: the text before the others differs."));
        }

        return taken;
    }

    private void Status(string message)
    {
        LastStatusMessage = message;
        StatusRequested?.Invoke(this, message);
    }

    // What the primary caret's text is: its replaced text (or its line's text before it, leading white space aside), and
    // whether the replaced text starts a word.
    private readonly record struct CaretReference(bool Valid, int Start, int Length, bool WordStart, int Indent, int IndentLength);

    private static CaretReference Reference(ITextSnapshot snapshot, int caret, CaretMatch match, int before, int after)
    {
        if (match == CaretMatch.ReplacedText)
        {
            int start = caret - before;
            if (start < 0 || caret + after > snapshot.Length)
            {
                return default;
            }

            bool wordStart = start == 0 || !CompletionController.IsIdentifierPart(snapshot[start - 1]);
            return new CaretReference(true, start, before + after, wordStart, 0, 0);
        }

        var line = snapshot.GetLine(snapshot.GetLineIndex(caret));
        int indent = IndentEnd(snapshot, line.Start, caret);
        return new CaretReference(true, indent, caret - indent, false, line.Start, indent - line.Start);
    }

    // Whether another caret takes the primary's text: an empty selection whose replaced range neither overlaps the one
    // taken before it nor the primary's (the primary always takes it), with the same text.
    private static bool Matches(ITextSnapshot snapshot, SelectionState selection, CaretReference reference, CaretMatch match, int before, int after, int lastEnd, int primaryCaret)
    {
        if (!selection.IsEmpty)
        {
            return false;
        }

        int caret = selection.Caret;
        if (match == CaretMatch.ReplacedText)
        {
            int start = caret - before;
            if (start < 0 || start < lastEnd || caret + after > snapshot.Length
                || (caret < primaryCaret ? caret + after > primaryCaret - before : start < primaryCaret + after))
            {
                return false;
            }

            if (reference.WordStart && start > 0 && CompletionController.IsIdentifierPart(snapshot[start - 1]))
            {
                return false;                                // "xWri" is not the word "Wri" the primary completes
            }

            return Same(snapshot, start, reference.Start, before + after);
        }

        if (caret < lastEnd)
        {
            return false;
        }

        var line = snapshot.GetLine(snapshot.GetLineIndex(caret));
        int indent = IndentEnd(snapshot, line.Start, caret);
        return caret - indent == reference.Length && Same(snapshot, indent, reference.Start, reference.Length);
    }

    private static bool Same(ITextSnapshot snapshot, int a, int b, int length)
    {
        if (a == b)
        {
            return true;
        }

        for (int i = 0; i < length; i++)
        {
            if (snapshot[a + i] != snapshot[b + i])
            {
                return false;
            }
        }

        return true;
    }

    private static int IndentEnd(ITextSnapshot snapshot, int from, int to)
    {
        int at = from;
        while (at < to && snapshot[at] is ' ' or '\t')
        {
            at++;
        }

        return at;
    }

    // The caret line's leading white space (read once per caret, on accept).
    private static string IndentOf(ITextSnapshot snapshot, int caret)
    {
        var line = snapshot.GetLine(snapshot.GetLineIndex(caret));
        int end = IndentEnd(snapshot, line.Start, line.End);
        return end == line.Start ? "" : snapshot.GetText(line.Start, end - line.Start);
    }

    // Ghost text's later lines in another caret's indent: a line that starts with the primary's indent takes the caret's.
    private string Reindent(string text, int primaryLineStart, string indent)
    {
        if (!text.Contains('\n', StringComparison.Ordinal))
        {
            return text;
        }

        var snapshot = Snapshot;
        int primaryEnd = IndentEnd(snapshot, primaryLineStart, snapshot.GetLine(snapshot.GetLineIndex(primaryLineStart)).End);
        string primaryIndent = primaryEnd == primaryLineStart ? "" : snapshot.GetText(primaryLineStart, primaryEnd - primaryLineStart);
        if (string.Equals(primaryIndent, indent, StringComparison.Ordinal))
        {
            return text;
        }

        var lines = text.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(primaryIndent, StringComparison.Ordinal))
            {
                lines[i] = string.Concat(indent, lines[i].AsSpan(primaryIndent.Length));
            }
        }

        return string.Join('\n', lines);
    }
}
