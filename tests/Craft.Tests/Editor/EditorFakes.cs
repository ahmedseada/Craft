// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Editor;
using Craft.Editor.Abstractions;

namespace Craft.Tests.Editor;

/// <summary>A snapshot over a string with its line starts (the editor's tests only; Craft.Core's piece table is the real one).</summary>
internal sealed class StringSnapshot : ITextSnapshot
{
    private readonly string _text;
    private readonly int[] _starts;

    public StringSnapshot(ITextBuffer buffer, string text, int version)
    {
        Buffer = buffer;
        _text = text;
        Version = version;
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                i++;
                starts.Add(i + 1);
            }
            else if (text[i] is '\r' or '\n')
            {
                starts.Add(i + 1);
            }
        }

        _starts = [.. starts];
    }

    public string Text => _text;

    public ITextBuffer Buffer { get; }

    public int Version { get; }

    public int Length => _text.Length;

    public int LineCount => _starts.Length;

    public char this[int position] => _text[position];

    public TextLine GetLine(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _starts.Length);
        int start = _starts[index];
        int next = index + 1 < _starts.Length ? _starts[index + 1] : _text.Length;
        int breakLength = 0;
        if (index + 1 < _starts.Length)
        {
            breakLength = next - start >= 2 && _text[next - 2] == '\r' && _text[next - 1] == '\n' ? 2 : 1;
        }

        return new TextLine(index, start, next - start - breakLength, breakLength);
    }

    public int GetLineIndex(int position)
    {
        int found = Array.BinarySearch(_starts, position);
        int index = found >= 0 ? found : ~found - 1;
        return Math.Clamp(index, 0, _starts.Length - 1);
    }

    public void CopyTo(int start, Span<char> destination) => _text.AsSpan(start, Math.Min(destination.Length, _text.Length - start)).CopyTo(destination);

    public string GetText(int start, int length) => _text.Substring(start, length);
}

/// <summary>A string-backed buffer.</summary>
internal sealed class StringBuffer : ITextBuffer
{
    private StringSnapshot _current;

    public StringBuffer(string text) => _current = new StringSnapshot(this, text, 0);

    public ITextSnapshot Current => _current;

    public string Text => _current.Text;

    public event EventHandler<TextChangedEventArgs>? Changed;

    public ITextSnapshot Apply(ReadOnlySpan<TextEdit> edits, TextChangeReason reason = TextChangeReason.Edit)
    {
        if (edits.IsEmpty)
        {
            return _current;
        }

        var before = _current;
        var text = new System.Text.StringBuilder();
        int at = 0;
        foreach (var edit in edits)
        {
            if (edit.Start < at || edit.End > before.Length)
            {
                throw new ArgumentException($"edit {edit} out of order or outside the text");
            }

            text.Append(before.Text, at, edit.Start - at).Append(edit.Text);
            at = edit.End;
        }

        text.Append(before.Text, at, before.Length - at);
        _current = new StringSnapshot(this, text.ToString(), before.Version + 1);
        Changed?.Invoke(this, new TextChangedEventArgs(before, _current, edits.ToArray(), reason));
        return _current;
    }
}

/// <summary>A document over <see cref="StringBuffer"/> with an undo stack of one step per edit (no grouping).</summary>
internal sealed class FakeDocument(string text, string? path = null) : ITextDocument
{
    private readonly StringBuffer _buffer = new(text);
    private readonly Stack<(TextEdit[] Inverse, TextEdit[] Forward, SelectionState Before, SelectionState After)> _undo = new();
    private readonly Stack<(TextEdit[] Inverse, TextEdit[] Forward, SelectionState Before, SelectionState After)> _redo = new();

    public ITextBuffer Buffer => _buffer;

    public string Text => _buffer.Text;

    public string? FilePath { get; } = path;

    public bool IsDirty => _undo.Count > 0;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public List<EditKind> Kinds { get; } = [];

    public event EventHandler? StateChanged;

    public ITextSnapshot Edit(ReadOnlySpan<TextEdit> edits, EditKind kind, SelectionState before, SelectionState after)
    {
        var snapshot = _buffer.Current;
        var forward = edits.ToArray();
        var inverse = Inverse(snapshot, forward);
        var result = _buffer.Apply(edits);
        _undo.Push((inverse, forward, before, after));
        _redo.Clear();
        Kinds.Add(kind);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public SelectionState? Undo()
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        var step = _undo.Pop();
        _buffer.Apply(step.Inverse, TextChangeReason.Undo);
        _redo.Push(step);
        return step.Before;
    }

    public SelectionState? Redo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var step = _redo.Pop();
        _buffer.Apply(step.Forward, TextChangeReason.Redo);
        _undo.Push(step);
        return step.After;
    }

    public Task SaveAsync(string? path = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose()
    {
    }

    private static TextEdit[] Inverse(ITextSnapshot snapshot, TextEdit[] edits)
    {
        var inverse = new TextEdit[edits.Length];
        int delta = 0;
        for (int i = 0; i < edits.Length; i++)
        {
            var edit = edits[i];
            inverse[i] = new TextEdit(edit.Start + delta, edit.Text.Length, snapshot.GetText(edit.Start, edit.Length));
            delta += edit.Delta;
        }

        return inverse;
    }
}

/// <summary>A clipboard held in a field.</summary>
internal sealed class FakeClipboard : IEditorClipboard
{
    public string? Text { get; set; }

    public Task<string?> GetTextAsync() => Task.FromResult(Text);

    public Task SetTextAsync(string text)
    {
        Text = text;
        return Task.CompletedTask;
    }
}

/// <summary>Fixed metrics: cells 10 wide, lines 20 high.</summary>
internal sealed class FixedMeasurer : ITextMeasurer
{
    public static readonly FontMetrics Metrics = new(10, 20, 15);

    public FontMetrics Measure(EditorFont font, float lineSpacing) => Metrics;
}

/// <summary>A clock the test moves.</summary>
internal sealed class ManualTime : TimeProvider
{
    public long Ticks { get; set; } = 1_000_000;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Ticks;

    public void AdvanceMilliseconds(int ms) => Ticks += ms * TimeSpan.TicksPerMillisecond;
}

/// <summary>A canvas that records what is drawn, in order.</summary>
internal sealed class RecordingCanvas : IEditorCanvas
{
    public List<(string Kind, float X, float Y, float W, float H, uint Argb, string? Text)> Calls { get; } = [];

    public IEnumerable<(string Text, float X, float Top, uint Argb)> Texts =>
        Calls.Where(c => c.Kind == "text").Select(c => (c.Text!, c.X, c.Y, c.Argb));

    public void Clear() => Calls.Clear();

    public void FillRect(float x, float y, float width, float height, uint argb) => Calls.Add(("fill", x, y, width, height, argb, null));

    public void DrawLine(float x1, float y1, float x2, float y2, uint argb, float thickness) => Calls.Add(("line", x1, y1, x2 - x1, y2 - y1, argb, null));

    public void DrawText(string text, float x, float top, in TextStyle style) => Calls.Add(("text", x, top, 0, 0, style.Argb, text));
}

/// <summary>A test language: "..." strings and // comments, classified per call over the range asked.</summary>
internal sealed class QuoteLanguage : ILanguage
{
    public int ClassifyCalls { get; set; }

    public int CharactersClassified { get; set; }

    public string Name => "quotes";

    public string Summary => "Test: strings in double quotes, // comments";

    public IReadOnlyList<string> Extensions { get; } = [".q"];

    public LanguageRules Rules { get; } = new("//", null, null, [new('(', ')'), new('[', ']'), new('{', '}')]);

    public ILanguageSession Open(ITextBuffer buffer) => new Session(this);

    private sealed class Session(QuoteLanguage language) : ILanguageSession
    {
        public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
        {
            language.ClassifyCalls++;
            language.CharactersClassified += length;
            int end = start + length;
            int i = start;
            while (i < end)
            {
                char c = snapshot[i];
                if (c == '"')
                {
                    int j = i + 1;
                    while (j < end && snapshot[j] != '"' && snapshot[j] != '\n')
                    {
                        j++;
                    }

                    j = Math.Min(end, j + 1);
                    into.Add(i, j - i, TokenClasses.String);
                    i = j;
                }
                else if (c == '/' && i + 1 < end && snapshot[i + 1] == '/')
                {
                    int j = i;
                    while (j < end && snapshot[j] != '\n' && snapshot[j] != '\r')
                    {
                        j++;
                    }

                    into.Add(i, j - i, TokenClasses.Comment);
                    i = j;
                }
                else
                {
                    i++;
                }
            }
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// A test language whose "parse" lands when the test says (<see cref="Land"/>, from another thread): until then it gives
/// no spans and its wait fails; after it, digits are numbers. It raises Reclassified on the landing thread.
/// </summary>
internal sealed class DeferredLanguage : ILanguage
{
    public Session? Last { get; private set; }

    public string Name => "deferred";

    public string Summary => "Test: spans that arrive after a background parse";

    public IReadOnlyList<string> Extensions { get; } = [".deferred"];

    public LanguageRules Rules => LanguageRules.Plain;

    public ILanguageSession Open(ITextBuffer buffer) => Last = new Session();

    internal sealed class Session : ILanguageSession
    {
        private volatile bool _landed;

        public event EventHandler? Reclassified;

        public int Waits { get; private set; }

        public void Land()
        {
            _landed = true;
            Reclassified?.Invoke(this, EventArgs.Empty);
        }

        public bool WaitUntilClassified(ITextSnapshot snapshot, TimeSpan timeout)
        {
            Waits++;
            return _landed;
        }

        public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
        {
            if (!_landed)
            {
                return;
            }

            for (int i = start; i < start + length; i++)
            {
                if (char.IsAsciiDigit(snapshot[i]))
                {
                    int j = i;
                    while (j < start + length && char.IsAsciiDigit(snapshot[j]))
                    {
                        j++;
                    }

                    into.Add(i, j - i, TokenClasses.Number);
                    i = j;
                }
            }
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>Builds an editor over a fake document.</summary>
internal static class Editors
{
    public static (EditorModel Model, FakeDocument Document, FakeClipboard Clipboard) Make(string text, CraftOptions? options = null, ILanguage? language = null,
        TimeProvider? time = null, string? path = null)
    {
        var document = new FakeDocument(text, path);
        var clipboard = new FakeClipboard();
        options ??= new CraftOptions();
        if (options.NewLine is null)
        {
            // The tests' new documents use "\n", whatever the running system's line break is.
            options = options with { NewLine = "\n" };
        }

        var model = new EditorModel(document, language, options, EditorThemes.Get(EditorThemes.Dark), new FixedMeasurer(), clipboard, time);
        model.UpdateViewport(0, 0, 800, 400);
        return (model, document, clipboard);
    }

    /// <summary>Text with "|" marking the caret (removed), or "[" and "]" marking a selection's anchor and caret.</summary>
    public static (EditorModel Model, FakeDocument Document, FakeClipboard Clipboard) MakeMarked(string marked, CraftOptions? options = null, ILanguage? language = null)
    {
        int caret = marked.IndexOf('|', StringComparison.Ordinal);
        string text;
        SelectionState selection;
        if (caret >= 0)
        {
            text = marked.Remove(caret, 1);
            selection = SelectionState.At(caret);
        }
        else
        {
            int anchor = marked.IndexOf('[', StringComparison.Ordinal);
            text = marked.Remove(anchor, 1);
            int end = text.IndexOf(']', StringComparison.Ordinal);
            text = text.Remove(end, 1);
            selection = new SelectionState(anchor, end);
        }

        var made = Make(text, options, language);
        made.Model.Select(selection.Anchor, selection.Caret);
        return made;
    }

    /// <summary>The document's text with "|" at the caret (nothing selected) or "[" ... "]" around the selection.</summary>
    public static string Marked(EditorModel model, FakeDocument document)
    {
        var s = model.Selection;
        if (s.IsEmpty)
        {
            return document.Text.Insert(s.Caret, "|");
        }

        return document.Text.Insert(s.End, "]").Insert(s.Start, "[");
    }
}
