// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;

namespace Craft.Editor;

/// <summary>
/// X part 9, the editing helpers: word wrap (the rows, the visual places of positions, Alt+Z), bracket pair colours, the
/// lightbulb, rendered white space and rulers (their columns), the save actions (trim the trailing white space, insert a
/// final newline) and a file's own settings (<c>.editorconfig</c>) over the options.
/// </summary>
public sealed partial class EditorModel
{
    private readonly CraftOptions _baseOptions;
    private WordWrap? _wordWrap;
    private BracketColors? _bracketColors;
    private Lightbulb? _lightbulb;
    private string? _rulersSource;
    private int? _rulersExtra;
    private int[] _rulers = [];

    /// <summary>Word wrap: the rows of the lines, the width, the mode in effect (Alt+Z switches it).</summary>
    public WordWrap WordWrap => _wordWrap ??= new WordWrap(this);

    /// <summary>Bracket pair colours: the depth at each line's start, kept per line.</summary>
    public BracketColors BracketColors => _bracketColors ??= new BracketColors(this);

    /// <summary>The lightbulb in the gutter where code actions exist (the host sets its <see cref="Lightbulb.Probe"/>).</summary>
    public Lightbulb Lightbulb => _lightbulb ??= new Lightbulb(this);

    /// <summary>The line the lightbulb shows on, or -1 (none is made until the host gives it a probe).</summary>
    public int LightbulbLine => _lightbulb?.Line ?? -1;

    /// <summary>The file's own settings in effect over the options (<see cref="FileSettings.None"/> when it has none).</summary>
    public FileSettings FileSettings { get; private set; } = FileSettings.None;

    /// <summary>The editor's options before the file's own settings (<see cref="Options"/> is these with them over).</summary>
    public CraftOptions BaseOptions => _baseOptions;

    /// <summary>Raised when a click on the lightbulb asks for Quick fix (the host runs <c>craft.quickFix</c>).</summary>
    public event EventHandler? QuickFixRequested;

    /// <summary>
    /// Takes <paramref name="settings"/> (a file's <c>.editorconfig</c>) over the editor's options: the tab size and
    /// indentation, the line break Enter inserts, trimming and the final newline on save, a ruler at the line length. The
    /// text is not changed; it redraws.
    /// </summary>
    public void ApplyFileSettings(FileSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        FileSettings = settings;
        var options = _baseOptions with
        {
            TabSize = settings.TabSize ?? _baseOptions.TabSize,
            InsertSpaces = settings.InsertSpaces ?? _baseOptions.InsertSpaces,
            NewLine = settings.NewLine ?? _baseOptions.NewLine,
            TrimTrailingWhitespace = settings.TrimTrailingWhitespace ?? _baseOptions.TrimTrailingWhitespace,
            InsertFinalNewline = settings.InsertFinalNewline ?? _baseOptions.InsertFinalNewline,
        };
        if (!Equals(options, Options))
        {
            Options = options;
            _indentUnit = options.InsertSpaces ? new string(' ', options.TabSize) : "\t";
            _bracketVersion = -1;
            ContentSizeChanged?.Invoke(this, EventArgs.Empty);
        }

        Invalidate();
    }

    /// <summary>The rows <paramref name="line"/> takes (more than 1 only when it is wrapped).</summary>
    public int RowsOfLine(int line) => WordWrap.IsActive ? WordWrap.RowsOf(line) : 1;

    /// <summary>
    /// The visual row and column of <paramref name="position"/>: its line's row (plus the wrapped row it is on) and its
    /// column on that row (tabs expanded; a continuation row's indent included).
    /// </summary>
    public (int Row, int Column) VisualOf(int position)
    {
        var snapshot = Snapshot;
        position = Math.Clamp(position, 0, snapshot.Length);
        int index = snapshot.GetLineIndex(position);
        var line = snapshot.GetLine(index);
        char[]? buffer = null;
        try
        {
            var text = TextReading.ReadLine(snapshot, line, ref buffer);
            int column = TextReading.ColumnOf(text, position - line.Start, Options.TabSize);
            if (!WordWrap.IsActive)
            {
                return (RowOfLine(index), column);
            }

            var wrapped = WordWrap.Line(index, text)!;
            int row = wrapped.RowOf(position - line.Start);
            return (RowOfLine(index) + row, wrapped.VisualColumn(row, column));
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    /// <summary>
    /// The position at visual <paramref name="column"/> of <paramref name="row"/>: the last character boundary at or before
    /// it, or the nearest with <paramref name="nearest"/>; on a wrapped row, never past the row's last character.
    /// </summary>
    public int PositionAtVisual(int row, float column, bool nearest)
    {
        var snapshot = Snapshot;
        row = Math.Clamp(row, 0, Math.Max(0, RowCount - 1));
        int index = LineAtRow(row);
        var line = snapshot.GetLine(index);
        if (!WordWrap.IsActive)
        {
            return PositionAtColumn(line, column, nearest);
        }

        char[]? buffer = null;
        try
        {
            var text = TextReading.ReadLine(snapshot, line, ref buffer);
            var wrapped = WordWrap.Line(index, text)!;
            int sub = Math.Clamp(row - RowOfLine(index), 0, wrapped.Rows - 1);
            float target = wrapped.StartColumn(sub) + Math.Max(0, column - wrapped.RowIndent(sub));
            int at = TextReading.IndexAtColumn(text, target, Options.TabSize, nearest);
            int start = wrapped.RowStart(sub);
            int end = wrapped.RowEnd(sub);
            if (sub + 1 < wrapped.Rows && at >= end)
            {
                // The row's end is the next row's start: the caret stays on this row, before its last character.
                at = Math.Max(start, end - 1);
                if (at > start && char.IsLowSurrogate(text[at]))
                {
                    at--;
                }
            }

            return line.Start + Math.Clamp(at, start, end);
        }
        finally
        {
            TextReading.Return(buffer);
        }
    }

    /// <summary>Switches word wrap for this editor (Alt+Z), keeping the line at the top where it was.</summary>
    public bool ToggleWordWrap()
    {
        int top = LineAtRow(VisibleRows(ScrollY, ViewportHeight).First);
        WordWrap.Toggle();
        float y = RowOfLine(top) * Metrics.LineHeight;
        ContentSizeChanged?.Invoke(this, EventArgs.Empty);
        ScrollToOffsets(WordWrap.Mode == "on" ? 0 : ScrollX, y, instant: true);
        EnsureCaretVisible();
        Invalidate();
        return true;
    }

    /// <summary>
    /// Removes the white space at the end of every line (Save does it first with <c>CRAFT_TRIM_TRAILING_WHITESPACE</c> or a
    /// file's <c>trim_trailing_whitespace</c>), as one undo step; false when there was none.
    /// </summary>
    public bool TrimTrailingWhitespace()
    {
        var edits = SaveActions.TrailingWhitespace(Snapshot);
        if (edits.Count == 0 || _readOnly)
        {
            return false;
        }

        var after = _selection;
        ApplyEdits(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edits), EditKind.Command, MapSelection(after, edits));
        return true;
    }

    /// <summary>
    /// Ends the text with a line break when it has none (Save does it first with <c>CRAFT_INSERT_FINAL_NEWLINE</c> or a file's
    /// <c>insert_final_newline</c>), as one undo step; false when it had one, or the text is empty.
    /// </summary>
    public bool InsertFinalNewline()
    {
        var snapshot = Snapshot;
        if (_readOnly || !SaveActions.NeedsFinalNewline(snapshot))
        {
            return false;
        }

        ApplyEdits([TextEdit.Insert(snapshot.Length, NewLine)], EditKind.Command, _selection);
        return true;
    }

    /// <summary>The diagnostics that start on <paramref name="line"/> (hidden ones left out), in order.</summary>
    public IReadOnlyList<EditorDiagnostic> DiagnosticsOnLine(int line)
    {
        var snapshot = Snapshot;
        if (_diagnostics.Length == 0 || line < 0 || line >= snapshot.LineCount)
        {
            return [];
        }

        var text = snapshot.GetLine(line);
        List<EditorDiagnostic>? found = null;
        for (int i = FirstDiagnosticReaching(text.Start); i < _diagnostics.Length && _diagnostics[i].Start <= text.End; i++)
        {
            var diagnostic = _diagnostics[i];
            if (diagnostic.Start >= text.Start && diagnostic.Severity != DiagnosticSeverity.Hidden)
            {
                (found ??= []).Add(diagnostic);
            }
        }

        return found is null ? [] : found;
    }

    /// <summary>The columns the vertical rulers mark: <c>CRAFT_RULERS</c>, and a file's <c>max_line_length</c> (parsed once per value).</summary>
    public ReadOnlySpan<int> RulerColumns
    {
        get
        {
            string source = Options.Rulers;
            int? extra = FileSettings.MaxLineLength;
            if (!ReferenceEquals(source, _rulersSource) || extra != _rulersExtra)
            {
                _rulersSource = source;
                _rulersExtra = extra;
                var columns = CraftOptions.ParseRulers(source);
                _rulers = extra is int length && Array.IndexOf(columns, length) < 0 ? [.. columns, length] : columns;
            }

            return _rulers;
        }
    }

    /// <summary>Asks the host for Quick fix (a click on the lightbulb).</summary>
    internal void RequestQuickFix() => QuickFixRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Raises <see cref="ContentSizeChanged"/> (the rows of wrapped lines changed).</summary>
    internal void RaiseContentSizeChanged() => ContentSizeChanged?.Invoke(this, EventArgs.Empty);

    // The selection after `edits` (in the positions before them, in order): each end moved by the edits before it.
    private static SelectionState MapSelection(SelectionState selection, List<TextEdit> edits)
    {
        int Map(int position)
        {
            int delta = 0;
            foreach (var edit in edits)
            {
                if (edit.End <= position)
                {
                    delta += edit.Delta;
                }
                else if (edit.Start < position)
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

        return new SelectionState(Map(selection.Anchor), Map(selection.Caret));
    }
}

/// <summary>The edits of the save actions (X part 9), on a snapshot, with or without an editor.</summary>
public static class SaveActions
{
    /// <summary>The edits that remove the spaces and tabs at the end of each line of <paramref name="snapshot"/>, in order.</summary>
    public static List<TextEdit> TrailingWhitespace(ITextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var edits = new List<TextEdit>();
        char[]? buffer = null;
        try
        {
            for (int i = 0; i < snapshot.LineCount; i++)
            {
                var line = snapshot.GetLine(i);
                if (line.Length == 0)
                {
                    continue;
                }

                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                int end = text.Length;
                while (end > 0 && text[end - 1] is ' ' or '\t')
                {
                    end--;
                }

                if (end < text.Length)
                {
                    edits.Add(TextEdit.Delete(line.Start + end, text.Length - end));
                }
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }

        return edits;
    }

    /// <summary>Whether <paramref name="snapshot"/> is not empty and does not end with a line break.</summary>
    public static bool NeedsFinalNewline(ITextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot.Length > 0 && snapshot[snapshot.Length - 1] is not ('\n' or '\r');
    }

    /// <summary>
    /// Applies the save actions to <paramref name="document"/> with no editor showing it (a document open only in the
    /// host): each one undo step of its own. Returns how many it applied.
    /// </summary>
    public static int Apply(ITextDocument document, bool trim, bool finalNewline, string newLine)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(newLine);
        int applied = 0;
        var at = SelectionState.At(0);
        if (trim && TrailingWhitespace(document.Buffer.Current) is { Count: > 0 } edits)
        {
            document.Edit(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edits), EditKind.Command, at, at);
            applied++;
        }

        var snapshot = document.Buffer.Current;
        if (finalNewline && NeedsFinalNewline(snapshot))
        {
            document.Edit([TextEdit.Insert(snapshot.Length, newLine)], EditKind.Command, at, at);
            applied++;
        }

        return applied;
    }
}
