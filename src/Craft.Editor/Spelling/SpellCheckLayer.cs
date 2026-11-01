// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Craft.Abstractions;

namespace Craft.Editor.Spelling;

/// <summary>Which characters of a line the spell check reads.</summary>
public enum SpellScope
{
    /// <summary>A language's comments and strings (their identifiers split by case and '_').</summary>
    CommentsAndStrings,

    /// <summary>Prose: every character but code, addresses and marks (plain text, Markdown).</summary>
    Prose,
}

/// <summary>
/// The spell check of one editor (X part 9, item 22): only the lines a frame draws, each line's unknown words worked out
/// once from its text and the frame's classification (its comments and strings, or a prose file's text) and kept while the
/// line's text, its spans and what the checker knows stay the same, so a warm frame checks nothing and allocates nothing
/// (it hashes each shown line's text and spans and draws the kept squiggles). The squiggles are faint, in the theme's
/// spelling colour. The quick fix (<see cref="SpellingCodeActions"/>) reads the unknown word at the caret from it.
/// </summary>
public sealed class SpellCheckLayer
{
    private static readonly ConditionalWeakTable<ITextDocument, SpellCheckLayer> Latest = new();
    private readonly EditorModel _model;
    private readonly Dictionary<int, Line> _lines = [];
    private (int Start, int Length)[] _scratch = new (int, int)[16];

    internal SpellCheckLayer(EditorModel model) => _model = model;

    /// <summary>The lines whose words were checked since it was made (each line once while it stays the same).</summary>
    public long LinesChecked { get; private set; }

    /// <summary>The words looked up since it was made.</summary>
    public long WordsChecked { get; private set; }

    /// <summary>The squiggles the last frame drew.</summary>
    public int SquigglesDrawn { get; private set; }

    /// <summary>The lines it keeps now (on screen and in the margin).</summary>
    public int LinesHeld => _lines.Count;

    /// <summary>The layer that last drew <paramref name="document"/> (the quick fix's way to its words), or null.</summary>
    public static SpellCheckLayer? Of(ITextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Latest.TryGetValue(document, out var layer) ? layer : null;
    }

    /// <summary>How <paramref name="language"/>'s documents are read: prose for none, plain text and Markdown; else comments and strings.</summary>
    public static SpellScope ScopeOf(ILanguage? language) =>
        language is null || language.Name.Equals("markdown", StringComparison.OrdinalIgnoreCase) || language.Name.Equals("plaintext", StringComparison.OrdinalIgnoreCase)
            ? SpellScope.Prose
            : SpellScope.CommentsAndStrings;

    /// <summary>
    /// The unknown word around <paramref name="position"/> of the current text, from what the last frame checked: its start,
    /// length and text; false when there is none (or its line was not drawn).
    /// </summary>
    public bool TryMissAt(int position, out int start, out int length, out string word)
    {
        start = length = 0;
        word = "";
        var snapshot = _model.Snapshot;
        if (position < 0 || position > snapshot.Length)
        {
            return false;
        }

        int index = snapshot.GetLineIndex(position);
        var line = snapshot.GetLine(index);
        if (!_lines.TryGetValue(index, out var kept))
        {
            return false;
        }

        string text = snapshot.GetText(line.Start, line.Length);
        if (kept.TextHash != string.GetHashCode(text) || kept.Length != text.Length)
        {
            return false;
        }

        for (int i = 0; i < kept.Count; i++)
        {
            var (from, count) = kept.Misses[i];
            if (position >= line.Start + from && position <= line.Start + from + count)
            {
                (start, length, word) = (line.Start + from, count, text.Substring(from, count));
                return true;
            }
        }

        return false;
    }

    /// <summary>The unknown words of the lines it keeps, as (line, start in the line, length): tests' and the telemetry's view.</summary>
    public IReadOnlyList<(int Line, int Start, int Length)> Misses()
    {
        var all = new List<(int, int, int)>();
        foreach (var (index, line) in _lines.OrderBy(p => p.Key))
        {
            for (int i = 0; i < line.Count; i++)
            {
                all.Add((index, line.Misses[i].Start, line.Misses[i].Length));
            }
        }

        return all;
    }

    /// <summary>Starts a frame over <paramref name="visible"/>: the lines kept outside it (and its margin) are dropped; false when nothing can be checked now.</summary>
    internal bool Begin(SpellChecker checker, LineRange visible, int margin)
    {
        SquigglesDrawn = 0;
        if (!checker.IsReady)
        {
            return false;
        }

        if (_lines.Count > (visible.Last - visible.First + 1) + (2 * margin) + 16)
        {
            int low = visible.First - margin, high = visible.Last + margin;
            List<int>? gone = null;
            foreach (var index in _lines.Keys)
            {
                if (index < low || index > high)
                {
                    (gone ??= []).Add(index);
                }
            }

            if (gone is not null)
            {
                foreach (var index in gone)
                {
                    _lines.Remove(index);
                }
            }
        }

        if (!Latest.TryGetValue(_model.Document, out var latest) || !ReferenceEquals(latest, this))
        {
            Latest.AddOrUpdate(_model.Document, this);
        }

        return true;
    }

    /// <summary>
    /// The squiggles of line <paramref name="index"/> (its text <paramref name="text"/>, starting at <paramref name="lineStart"/>;
    /// <paramref name="spans"/> the frame's spans from the line's first): its unknown words, checked when the line changed.
    /// </summary>
    internal void DrawLine(IEditorCanvas canvas, SpellChecker checker, SpellScope scope, int index, int lineStart, ReadOnlySpan<char> text, ReadOnlySpan<TokenSpan> spans,
        float left, float top, float cell, float height, int tabSize, float textLeft, float viewportWidth, uint color)
    {
        int textHash = string.GetHashCode(text);
        int spanHash = SpanHash(spans, lineStart, lineStart + text.Length);
        int version = checker.Version;
        ref var kept = ref CollectionsMarshal.GetValueRefOrAddDefault(_lines, index, out bool exists);
        if (!exists || kept.TextHash != textHash || kept.Length != text.Length || kept.SpanHash != spanHash || kept.Version != version)
        {
            kept = Check(checker, scope, lineStart, text, spans);
            kept.TextHash = textHash;
            kept.SpanHash = spanHash;
            kept.Version = version;
        }

        for (int i = 0; i < kept.Count; i++)
        {
            var (start, length) = kept.Misses[i];
            int from = TextReading.ColumnOf(text, start, tabSize);
            int to = TextReading.ColumnOf(text, start + length, tabSize);
            float x0 = Math.Max(textLeft, left + (from * cell));
            float x1 = Math.Min(viewportWidth, left + (to * cell));
            if (x1 <= x0)
            {
                continue;
            }

            // A faint wave, lower and flatter than a diagnostic's squiggle.
            float baseY = top + height - 1f;
            float step = Math.Max(2f, cell / 2.5f);
            bool up = false;
            for (float x = x0; x < x1; x += step)
            {
                float next = Math.Min(x1, x + step);
                canvas.DrawLine(x, up ? baseY - 1.5f : baseY, next, up ? baseY : baseY - 1.5f, color, 1);
                up = !up;
            }

            SquigglesDrawn++;
        }
    }

    // A line's unknown words (cold: only when the line, its spans or the checker changed).
    private Line Check(SpellChecker checker, SpellScope scope, int lineStart, ReadOnlySpan<char> text, ReadOnlySpan<TokenSpan> spans)
    {
        LinesChecked++;
        int count = 0;
        int minimum = checker.MinLength;
        foreach (var (start, length) in SpellWords.Of(text))
        {
            if (length < minimum || !Checked(scope, spans, lineStart + start, length) || IsAcronym(text.Slice(start, length))
                || SpellWords.InUrlOrPath(text, start, length) || NextToDigit(text, start, length))
            {
                continue;
            }

            WordsChecked++;
            if (checker.IsKnown(text.Slice(start, length)))
            {
                continue;
            }

            if (count == _scratch.Length)
            {
                Array.Resize(ref _scratch, count * 2);
            }

            _scratch[count++] = (start, length);
        }

        return new Line { Length = text.Length, Count = count, Misses = count == 0 ? [] : _scratch[..count] };
    }

    // Whether the word at position (of length) is read: inside a comment or a string (a language), or not inside code (prose).
    private static bool Checked(SpellScope scope, ReadOnlySpan<TokenSpan> spans, int position, int length)
    {
        string? cls = null;
        foreach (var span in spans)
        {
            if (span.Start > position)
            {
                break;
            }

            if (span.End >= position + length)
            {
                cls = span.Class;
            }
        }

        if (scope == SpellScope.Prose)
        {
            return cls is null || !(cls.StartsWith("string", StringComparison.Ordinal) || cls == TokenClasses.Punctuation || cls == TokenClasses.Operator
                || cls == TokenClasses.Number || cls == TokenClasses.ClassName);
        }

        return cls is not null && (IsComment(cls) || (cls.StartsWith("string", StringComparison.Ordinal) && cls != TokenClasses.StringEscape));
    }

    private static bool IsComment(string cls) =>
        cls == TokenClasses.Comment || cls == TokenClasses.DocComment || (cls.Contains("comment", StringComparison.Ordinal) && !cls.Contains("delimiter", StringComparison.Ordinal)
            && !cls.Contains("name", StringComparison.Ordinal) && !cls.Contains("attribute", StringComparison.Ordinal));

    // "HTML", "UTF": an acronym is passed over.
    private static bool IsAcronym(ReadOnlySpan<char> word)
    {
        foreach (char c in word)
        {
            if (!char.IsUpper(c))
            {
                return false;
            }
        }

        return true;
    }

    // "x2", "utf8": a word joined to a digit is a name, not prose (camelCase and snake_case parts are still read).
    private static bool NextToDigit(ReadOnlySpan<char> text, int start, int length) =>
        (start > 0 && char.IsAsciiDigit(text[start - 1])) || (start + length < text.Length && char.IsAsciiDigit(text[start + length]));

    private static int SpanHash(ReadOnlySpan<TokenSpan> spans, int start, int end)
    {
        var hash = new HashCode();
        foreach (var span in spans)
        {
            if (span.Start >= end)
            {
                break;
            }

            if (span.End > start)
            {
                hash.Add(span.Start - start);
                hash.Add(span.Length);
                hash.Add(string.GetHashCode(span.Class));
            }
        }

        return hash.ToHashCode();
    }

    private struct Line
    {
        public int TextHash;
        public int SpanHash;
        public int Length;
        public int Version;
        public int Count;
        public (int Start, int Length)[] Misses;
    }
}
