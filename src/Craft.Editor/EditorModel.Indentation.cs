// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

// X part 9, item 26: the status bar's indentation and line ending pickers act on the editor: the indentation chosen for
// this editor, the indentation and the line breaks converted, each as one undo step.
public sealed partial class EditorModel
{
    /// <summary>The indentation picked for this editor (the status bar's picker), over the options and the file's settings; null when none was.</summary>
    public (bool InsertSpaces, int TabSize)? IndentationOverride { get; private set; }

    /// <summary>
    /// Indents with spaces or tabs at <paramref name="tabSize"/> in this editor from now on (the status bar's picker), over
    /// the options and the file's <c>.editorconfig</c>; the text is not changed.
    /// </summary>
    public void SetIndentation(bool insertSpaces, int tabSize)
    {
        IndentationOverride = (insertSpaces, Math.Clamp(tabSize, 1, 32));
        ApplyFileSettings(FileSettings);
    }

    /// <summary>
    /// Rewrites every line's leading white space as spaces (<paramref name="toSpaces"/>) or as tabs where they fill one (the
    /// rest spaces), at the tab size in effect, as one undo step; false when no line changed.
    /// </summary>
    public bool ConvertIndentation(bool toSpaces)
    {
        if (_readOnly)
        {
            return false;
        }

        var snapshot = Snapshot;
        int tabSize = Math.Max(1, Options.TabSize);
        var edits = new List<TextEdit>();
        char[]? buffer = null;
        try
        {
            for (int index = 0; index < snapshot.LineCount; index++)
            {
                var line = snapshot.GetLine(index);
                var text = TextReading.ReadLine(snapshot, line, ref buffer);
                int length = 0;
                int column = 0;
                while (length < text.Length && text[length] is ' ' or '\t')
                {
                    column = text[length] == '\t' ? column + tabSize - (column % tabSize) : column + 1;
                    length++;
                }

                if (length == 0 || !NeedsConversion(text[..length], toSpaces, tabSize, column))
                {
                    continue;
                }

                edits.Add(new TextEdit(line.Start, length, toSpaces ? new string(' ', column) : new string('\t', column / tabSize) + new string(' ', column % tabSize)));
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }

        if (edits.Count == 0)
        {
            return false;
        }

        ApplyEdits(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edits), EditKind.Command, MapSelection(_selection, edits));
        return true;
    }

    /// <summary>
    /// Makes every line break of the text <paramref name="newLine"/> ("\n" or "\r\n"), as one undo step; the breaks the editor
    /// inserts follow it (a file's <c>.editorconfig</c> end_of_line still wins for those); false when every break was it already.
    /// </summary>
    public bool ConvertLineEndings(string newLine)
    {
        ArgumentException.ThrowIfNullOrEmpty(newLine);
        if (_readOnly)
        {
            return false;
        }

        var snapshot = Snapshot;
        var edits = new List<TextEdit>();
        for (int index = 0; index < snapshot.LineCount; index++)
        {
            var line = snapshot.GetLine(index);
            if (line.BreakLength == 0)
            {
                continue;
            }

            bool same = line.BreakLength == newLine.Length && (newLine.Length == 1 ? snapshot[line.End] == newLine[0] : snapshot[line.End] == '\r');
            if (!same)
            {
                edits.Add(new TextEdit(line.End, line.BreakLength, newLine));
            }
        }

        if (edits.Count == 0)
        {
            return false;
        }

        ApplyEdits(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(edits), EditKind.Command, MapSelection(_selection, edits));
        return true;
    }

    // Whether a line's leading white space differs from what the conversion writes for its columns.
    private static bool NeedsConversion(ReadOnlySpan<char> leading, bool toSpaces, int tabSize, int columns)
    {
        if (toSpaces)
        {
            return leading.Contains('\t');
        }

        int tabs = columns / tabSize;
        int spaces = columns % tabSize;
        if (leading.Length != tabs + spaces)
        {
            return true;
        }

        for (int i = 0; i < leading.Length; i++)
        {
            if (leading[i] != (i < tabs ? '\t' : ' '))
            {
                return true;
            }
        }

        return false;
    }
}
