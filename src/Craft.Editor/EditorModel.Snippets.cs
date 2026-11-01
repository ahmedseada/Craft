// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// Snippets (X part 9): a snippet committed from completion is expanded where it goes (the line's indentation on its
/// next lines, a tab as the editor's indent, the variables), inserted as one undo step, and its tab stops are kept as
/// ranges that follow every edit. Tab goes to the next stop, Shift+Tab to the one before; a stop that appears several
/// times is selected at each place at once (several carets), so typing fills them all; a stop with choices opens them as
/// a list; Escape, a caret outside the snippet, or reaching <c>$0</c> leaves it.
/// </summary>
public sealed partial class EditorModel
{
    private SnippetSession? _snippet;

    /// <summary>The snippet being filled in, or null.</summary>
    public SnippetSession? ActiveSnippet => _snippet;

    /// <summary>Whether completion offers snippets here (the setting on and a provider registered).</summary>
    internal bool OffersSnippets => Options.Snippets && SnippetProviders.All.Count > 0;

    /// <summary>
    /// Expands <paramref name="snippet"/> in place of <paramref name="start"/> to <paramref name="end"/> of the current
    /// text, as one undo step, and selects its first stop. False for a read-only editor.
    /// </summary>
    public bool InsertSnippet(Snippet snippet, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        return InsertSnippet(snippet.Body, start, end);
    }

    /// <summary>Expands the snippet <paramref name="body"/> (VS Code's syntax) in place of <paramref name="start"/> to <paramref name="end"/>, as <see cref="InsertSnippet(Snippet, int, int)"/>.</summary>
    public bool InsertSnippet(string body, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (_readOnly)
        {
            RefusedEdits++;
            return false;
        }

        var snapshot = Snapshot;
        start = Math.Clamp(start, 0, snapshot.Length);
        end = Math.Clamp(end, start, snapshot.Length);
        var line = snapshot.GetLine(snapshot.GetLineIndex(start));
        int indentEnd = line.Start;
        while (indentEnd < start && snapshot[indentEnd] is ' ' or '\t')
        {
            indentEnd++;
        }

        string selected = _selection.IsEmpty ? "" : snapshot.GetText(_selection.Start, _selection.Length);
        var context = new SnippetContext
        {
            Indent = snapshot.GetText(line.Start, indentEnd - line.Start),
            IndentUnit = _indentUnit,
            NewLine = NewLineOf(snapshot),
            Variables = name => SnippetVariable(name, snapshot, start, selected),
        };
        var expanded = SnippetParser.Expand(body, context);
        _snippet = null;
        Completion.Close();
        var edit = new TextEdit(start, end - start, expanded.Text);
        ApplyEdits(new ReadOnlySpan<TextEdit>(in edit), EditKind.Command, SelectionState.At(start + expanded.Text.Length));
        var session = new SnippetSession(start, start + expanded.Text.Length, expanded, Snapshot.Version);
        if (session.Order.Count <= 1 && session.StopsOf(0) is [{ } only])
        {
            // Nothing to fill: the caret goes to $0 and the snippet is done.
            SetSelection(SelectionState.At(only.Start));
            EnsureCaretVisible();
            return true;
        }

        _snippet = session;
        SelectStop(0);
        return true;
    }

    /// <summary>Replaces every selection with <paramref name="text"/> (a snippet stop's choice at each of its places), as one undo step, the carets after it.</summary>
    internal void ReplaceSelections(string text)
    {
        if (_readOnly)
        {
            RefusedEdits++;
            return;
        }

        EditCarets(EditKind.Command, keepAutoClosed: false, text, static (model, selection, snapshot, typed) =>
            new CaretPart(selection.Start, selection.Length, typed, typed.Length, typed.Length));
        EnsureCaretVisible();
    }

    /// <summary>Leaves the snippet (its stops are forgotten; the text and the carets stay).</summary>
    public void LeaveSnippet()
    {
        if (_snippet is null)
        {
            return;
        }

        _snippet = null;
        Invalidate();
    }

    /// <summary>Goes to the next stop (<paramref name="forward"/>) or the one before; false when there is no snippet.</summary>
    public bool NextSnippetStop(bool forward = true)
    {
        if (_snippet is not { } session || !CaretInSnippet(session))
        {
            LeaveSnippet();
            return false;
        }

        int next = session.Current + (forward ? 1 : -1);
        if (next < 0)
        {
            return true;
        }

        if (next >= session.Order.Count)
        {
            LeaveSnippet();
            return true;
        }

        SelectStop(next);
        return true;
    }

    // Tab, Shift+Tab and Escape while a snippet is filled in (before the editor's own keys); true when handled.
    private bool SnippetKey(EditorKey key, KeyModifiers modifiers)
    {
        if (_snippet is null || (modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0)
        {
            return false;
        }

        switch (key)
        {
            case EditorKey.Tab:
                return NextSnippetStop(forward: (modifiers & KeyModifiers.Shift) == 0);
            case EditorKey.Escape:
                LeaveSnippet();
                CollapseCarets();
                return true;
            default:
                return false;
        }
    }

    private void SelectStop(int orderIndex)
    {
        if (_snippet is not { } session)
        {
            return;
        }

        session.Current = orderIndex;
        int number = session.Order[orderIndex];
        var stops = session.StopsOf(number);
        if (stops.Count == 0)
        {
            LeaveSnippet();
            return;
        }

        var selections = new SelectionState[stops.Count];
        for (int i = 0; i < stops.Count; i++)
        {
            selections[i] = new SelectionState(stops[i].Start, stops[i].End);
        }

        SetSelections(selections, 0);
        EnsureCaretVisible();
        if (number == 0)
        {
            // The final caret: the snippet is done.
            _snippet = null;
            return;
        }

        if (stops[0].Choices.Count > 1)
        {
            Completion.ShowChoices(stops[0].Choices, stops[0].Start, stops[0].End);
        }
    }

    private bool CaretInSnippet(SnippetSession session)
    {
        int caret = _selection.Caret;
        return caret >= session.Start && caret <= session.End;
    }

    // Each buffer change moves the snippet's stops (an edit elsewhere only shifts them).
    private void MapSnippet(TextChangedEventArgs change)
    {
        if (_snippet is not { } session)
        {
            return;
        }

        if (!session.Map(change.Edits))
        {
            _snippet = null;
        }
    }

    // The values of VS Code's snippet variables; null for an unknown one (its default, else its name, is inserted).
    private string? SnippetVariable(string name, ITextSnapshot snapshot, int position, string selected)
    {
        var now = DateTime.Now;
        var culture = CultureInfo.InvariantCulture;
        int lineIndex = snapshot.GetLineIndex(position);
        string? file = _document.FilePath;
        return name switch
        {
            "TM_FILENAME" => file is null ? "untitled" : Path.GetFileName(file),
            "TM_FILENAME_BASE" => file is null ? "untitled" : Path.GetFileNameWithoutExtension(file),
            "TM_DIRECTORY" => file is null ? "" : Path.GetDirectoryName(file) ?? "",
            "TM_FILEPATH" => file ?? "",
            "TM_SELECTED_TEXT" => selected,
            "TM_CURRENT_LINE" => snapshot.GetText(snapshot.GetLine(lineIndex).Start, snapshot.GetLine(lineIndex).Length),
            "TM_LINE_INDEX" => lineIndex.ToString(culture),
            "TM_LINE_NUMBER" => (lineIndex + 1).ToString(culture),
            "WORKSPACE_NAME" => WorkspaceRoot is { } root ? Path.GetFileName(Path.TrimEndingDirectorySeparator(root)) : "",
            "WORKSPACE_FOLDER" => WorkspaceRoot ?? "",
            "CURRENT_YEAR" => now.ToString("yyyy", culture),
            "CURRENT_YEAR_SHORT" => now.ToString("yy", culture),
            "CURRENT_MONTH" => now.ToString("MM", culture),
            "CURRENT_MONTH_NAME" => now.ToString("MMMM", culture),
            "CURRENT_MONTH_NAME_SHORT" => now.ToString("MMM", culture),
            "CURRENT_DATE" => now.ToString("dd", culture),
            "CURRENT_DAY_NAME" => now.ToString("dddd", culture),
            "CURRENT_DAY_NAME_SHORT" => now.ToString("ddd", culture),
            "CURRENT_HOUR" => now.ToString("HH", culture),
            "CURRENT_MINUTE" => now.ToString("mm", culture),
            "CURRENT_SECOND" => now.ToString("ss", culture),
            "CURRENT_SECONDS_UNIX" => DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(culture),
            "UUID" => Guid.NewGuid().ToString(),
            "RANDOM" => Random.Shared.Next(0, 1_000_000).ToString("D6", culture),
            "RANDOM_HEX" => Random.Shared.Next(0, 0x1000000).ToString("x6", culture),
            "LINE_COMMENT" => Rules.LineComment ?? "",
            "BLOCK_COMMENT_START" => Rules.BlockCommentOpen ?? "",
            "BLOCK_COMMENT_END" => Rules.BlockCommentClose ?? "",
            _ => null,
        };
    }

    private string NewLineOf(ITextSnapshot snapshot)
    {
        if (Options.NewLine is { } configured)
        {
            return configured;
        }

        if (snapshot.LineCount > 1)
        {
            var first = snapshot.GetLine(0);
            return first.BreakLength == 2 ? "\r\n" : snapshot[first.End] == '\r' ? "\r" : "\n";
        }

        return Environment.NewLine;
    }
}

/// <summary>
/// A snippet being filled in (<see cref="EditorModel.ActiveSnippet"/>): its stops as ranges of the current text, the
/// order Tab visits them in, and the stop the caret is at. The ranges follow every edit (a placeholder grows as it is typed over).
/// </summary>
public sealed class SnippetSession
{
    private readonly List<Stop> _stops = [];

    internal SnippetSession(int start, int end, ExpandedSnippet expanded, int version)
    {
        Start = start;
        End = end;
        Version = version;
        foreach (var stop in expanded.Stops)
        {
            _stops.Add(new Stop(stop.Index, start + stop.Start, start + stop.Start + stop.Length, stop.Choices));
        }

        Order = expanded.Order;
    }

    /// <summary>Where the snippet's text starts now.</summary>
    public int Start { get; private set; }

    /// <summary>Where it ends now.</summary>
    public int End { get; private set; }

    /// <summary>The text version it was inserted in.</summary>
    public int Version { get; }

    /// <summary>The stop numbers in Tab's order (1, 2, …, then 0).</summary>
    public IReadOnlyList<int> Order { get; }

    /// <summary>The index in <see cref="Order"/> of the stop the caret is at.</summary>
    public int Current { get; internal set; }

    /// <summary>The number of the stop the caret is at.</summary>
    public int CurrentStop => Order[Current];

    /// <summary>Every stop's place now, in the order of the text.</summary>
    public IReadOnlyList<Stop> Stops => _stops;

    /// <summary>The places of stop <paramref name="number"/> (several when it mirrors), in the order of the text.</summary>
    public IReadOnlyList<Stop> StopsOf(int number)
    {
        var found = new List<Stop>();
        foreach (var stop in _stops)
        {
            if (stop.Index == number)
            {
                found.Add(stop);
            }
        }

        return found;
    }

    // Moves every range through the edits (in the old positions, in order): a range's start stays before text inserted at
    // it, its end goes after it, so typing over a placeholder or at its end grows it. False when the snippet's text is gone.
    internal bool Map(IReadOnlyList<TextEdit> edits)
    {
        Start = MapStart(Start, edits);
        End = MapEnd(End, edits);
        for (int i = 0; i < _stops.Count; i++)
        {
            var stop = _stops[i];
            int start = MapStart(stop.Start, edits);
            int end = Math.Max(start, MapEnd(stop.End, edits));
            _stops[i] = stop with { Start = start, End = end };
        }

        return End >= Start;
    }

    private static int MapStart(int position, IReadOnlyList<TextEdit> edits)
    {
        int delta = 0;
        foreach (var edit in edits)
        {
            if (position > edit.End || (position == edit.End && edit.Length > 0))
            {
                delta += edit.Text.Length - edit.Length;
            }
            else if (position > edit.Start)
            {
                return edit.Start + delta;
            }
            else
            {
                break;
            }
        }

        return position + delta;
    }

    private static int MapEnd(int position, IReadOnlyList<TextEdit> edits)
    {
        int delta = 0;
        foreach (var edit in edits)
        {
            if (position >= edit.End)
            {
                delta += edit.Text.Length - edit.Length;
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

    /// <summary>A stop's place in the current text.</summary>
    /// <param name="Index">Its number.</param>
    /// <param name="Start">Where it starts.</param>
    /// <param name="End">Where it ends.</param>
    /// <param name="Choices">Its choices, or none.</param>
    public sealed record Stop(int Index, int Start, int End, IReadOnlyList<string> Choices);
}
