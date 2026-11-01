// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// Linked editing (X part 9): typing, Backspace or Delete in a markup start tag's name (or its end tag's) does the same
/// in the other, in the same undo step (<c>CRAFT_LINKED_EDITING</c>). Before the keystroke the language is asked for the
/// ranges linked to the caret's (<see cref="ILanguageSession.GetLinkedEditingRanges"/>, from its tokenizer's states);
/// when the caret's selection lies in one and every range holds the same name, the edit runs at a second caret at the
/// same place in the other, and the editor is back to one caret after it.
/// </summary>
public sealed partial class EditorModel
{
    private enum LinkedEdit
    {
        Type,
        Backspace,
        Delete,
    }

    /// <summary>How many keystrokes were applied to linked ranges (the tests read it).</summary>
    public int LinkedEdits { get; private set; }

    private bool BackspaceLinked(bool control)
    {
        bool linked = !control && BeginLinked(LinkedEdit.Backspace, null);
        bool handled = Backspace(control);
        if (linked)
        {
            EndLinked();
        }

        return handled;
    }

    private bool DeleteForwardLinked(bool control)
    {
        bool linked = !control && BeginLinked(LinkedEdit.Delete, null);
        bool handled = DeleteForward(control);
        if (linked)
        {
            EndLinked();
        }

        return handled;
    }

    // Sets the second caret in the linked range when the keystroke stays inside the caret's range; true when it did.
    private bool BeginLinked(LinkedEdit kind, string? text)
    {
        if (!Options.LinkedEditing || _readOnly || HasMultipleCarets || _session is not { } session || _snippet is not null)
        {
            return false;
        }

        if (text is not null)
        {
            foreach (char c in text)
            {
                if (!IsNameCharacter(c))
                {
                    return false;
                }
            }
        }

        var snapshot = Snapshot;
        var selection = _selection;
        if (session.GetLinkedEditingRanges(snapshot, selection.Caret, Options.BracketScanLines) is not { Ranges.Count: >= 2 } linked)
        {
            return false;
        }

        var own = linked.Ranges[0];
        if (selection.Start < own.Start || selection.End > own.End)
        {
            return false;
        }

        // The keystroke must stay in the name: Backspace at its start or Delete at its end would join it to what is beside it.
        if (selection.IsEmpty && ((kind == LinkedEdit.Backspace && selection.Caret == own.Start) || (kind == LinkedEdit.Delete && selection.Caret == own.End)))
        {
            return false;
        }

        Span<SelectionState> carets = stackalloc SelectionState[2];
        carets[0] = selection;
        int count = 1;
        for (int i = 1; i < linked.Ranges.Count && count < carets.Length; i++)
        {
            var other = linked.Ranges[i];
            if (other.Length != own.Length || !SameText(snapshot, own.Start, other.Start, own.Length) || (other.Start < own.End && own.Start < other.End))
            {
                return false;
            }

            carets[count++] = new SelectionState(other.Start + (selection.Anchor - own.Start), other.Start + (selection.Caret - own.Start));
        }

        SetSelectionsCore(carets[..count], 0, keepDesiredColumns: false, keepAutoClosed: true, keepBox: false);
        LinkedEdits++;
        return HasMultipleCarets;
    }

    // Back to the one caret the user moves (the primary), once the linked edit is made.
    private void EndLinked()
    {
        var primary = _selection;
        SetSelectionsCore(new ReadOnlySpan<SelectionState>(in primary), 0, keepDesiredColumns: false, keepAutoClosed: true, keepBox: false);
    }

    private static bool SameText(ITextSnapshot snapshot, int a, int b, int length)
    {
        for (int i = 0; i < length; i++)
        {
            if (snapshot[a + i] != snapshot[b + i])
            {
                return false;
            }
        }

        return true;
    }

    // A markup name's characters (XML's names: letters, digits, '_', '-', '.', ':').
    private static bool IsNameCharacter(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':';
}
