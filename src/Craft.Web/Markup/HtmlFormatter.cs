// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;
using Craft.Web.Script;
using Craft.Web.Style;

namespace Craft.Web.Markup;

/// <summary>
/// The built-in formatter of HTML and Angular templates (W-markup): only the white space between tokens changes. Each line
/// is indented by the elements and Angular blocks open at its start (void elements and optional end tags known: an
/// <c>&lt;li&gt;</c> closes the open one, a block element closes an open <c>&lt;p&gt;</c>), a start tag's attribute lines
/// one step in and its <c>&gt;</c> line back at the tag; inside a tag, runs of spaces made one, none around <c>=</c> nor
/// before <c>&gt;</c>, one before <c>/&gt;</c>; <c>@if (…) {</c> and <c>} @else {</c> spaced; trailing spaces off, at most
/// one empty line. Text keeps its spaces; <c>&lt;pre&gt;</c>, <c>&lt;textarea&gt;</c>, comments, multi-line attribute
/// values and interpolations are kept as written. A <c>&lt;script&gt;</c>'s JavaScript and a <c>&lt;style&gt;</c>'s CSS are
/// formatted by their own formatters one step inside the element (left as written when they do not parse). A malformed
/// text (an element, a tag, a comment, a block or an interpolation not closed; an end tag that closes nothing or would
/// close an element whose end tag is required) is left unchanged, the message naming the line and the column.
/// </summary>
public sealed class HtmlFormatter : ITextFormatter
{
    private readonly HtmlTokenizer _tokenizer;

    /// <summary>A formatter reading with <paramref name="tokenizer"/> (HTML, or an Angular template of its version).</summary>
    public HtmlFormatter(HtmlTokenizer tokenizer)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        _tokenizer = tokenizer;
    }

    /// <inheritdoc />
    public void Format(ReadOnlySpan<char> text, FormatStyle style, List<TextEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(edits);
        var read = Read(_tokenizer, text);
        var own = new List<TextEdit>();
        new Layout(text, style, read, _tokenizer.Options.Embedded, own).Run();
        own.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        edits.AddRange(own);
    }

    internal readonly record struct Tok(int Start, int End, MarkupTokenKind Kind, int Line)
    {
        public int Length => End - Start;
    }

    internal sealed class Collector : IMarkupSink
    {
        public List<Tok> Tokens { get; } = [];

        public int Line { get; set; }

        public void Token(int start, int length, MarkupTokenKind kind) => Tokens.Add(new Tok(start, start + length, kind, Line));
    }

    internal sealed record Reading(List<Tok> Tokens, MarkupMode[] LineModes, int[] LineStarts, MarkupMode EndMode);

    // Lexes every line with the sink: the tokens, the mode each line starts in, the mode the text ends in.
    internal static Reading Read(HtmlTokenizer tokenizer, ReadOnlySpan<char> text)
    {
        var collector = new Collector();
        var modes = new List<MarkupMode>();
        var starts = new List<int>();
        ulong state = tokenizer.Initial;
        int start = 0;
        int line = 0;
        while (true)
        {
            int length = text[start..].IndexOfAny('\r', '\n');
            int end = length < 0 ? text.Length : start + length;
            modes.Add(tokenizer.ModeOf(state, out _, out _));
            starts.Add(start);
            collector.Line = line;
            state = tokenizer.Lex(text[start..end], start, state, null, collector);
            if (length < 0)
            {
                break;
            }

            start = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
            line++;
        }

        return new Reading(collector.Tokens, [.. modes], [.. starts], tokenizer.ModeOf(state, out _, out _));
    }

    private enum Open : byte
    {
        StartTag,
        EndTag,
        Element,
        Block,
    }

    private struct Entry
    {
        public Open Kind;
        public int NameStart;
        public int NameLength;
        public int LineIndent;
        public int At;
        public bool Optional;
        public bool Verbatim;
        public ElementKind Raw;
        public ScriptKind Script;
        public int ContentStart;
    }

    private ref struct Layout
    {
        private static readonly SearchValues<char> Blank = SearchValues.Create(" \t\r\n");
        private readonly ReadOnlySpan<char> _text;
        private readonly FormatStyle _style;
        private readonly Reading _read;
        private readonly List<Tok> _tokens;
        private readonly bool _embedded;
        private readonly List<TextEdit> _edits;
        private readonly List<Entry> _open = [];
        private int _lineIndent;
        private int _verbatim;

        public Layout(ReadOnlySpan<char> text, FormatStyle style, Reading read, bool embedded, List<TextEdit> edits)
        {
            _text = text;
            _style = style;
            _read = read;
            _tokens = read.Tokens;
            _embedded = embedded;
            _edits = edits;
        }

        public void Run()
        {
            var tokens = _tokens;
            for (int k = 0; k <= tokens.Count; k++)
            {
                int gapStart = k == 0 ? 0 : tokens[k - 1].End;
                int gapEnd = k == tokens.Count ? _text.Length : tokens[k].Start;
                var gap = _text[gapStart..gapEnd];
                string? wanted = Gap(k, gap);
                if (wanted is not null && gap.ContainsAnyExcept(Blank))
                {
                    wanted = null;
                }

                if (wanted is not null && !gap.SequenceEqual(wanted))
                {
                    _edits.Add(new TextEdit(gapStart, gapEnd - gapStart, wanted));
                }

                if (k < tokens.Count)
                {
                    Took(k);
                }
            }

            Finish();
        }

        private readonly ReadOnlySpan<char> Text(Tok t) => _text.Slice(t.Start, t.Length);

        private readonly ReadOnlySpan<char> NameOf(Entry e) => _text.Slice(e.NameStart, e.NameLength);

        private string? Gap(int k, ReadOnlySpan<char> gap)
        {
            var tokens = _tokens;
            if (k == tokens.Count)
            {
                return k == 0 || _verbatim > 0 || _read.EndMode != MarkupMode.Text ? null : gap.ContainsAny('\r', '\n') ? _style.NewLine : "";
            }

            if (k == 0)
            {
                _lineIndent = 0;
                return "";
            }

            int breaks = Breaks(gap);
            if (breaks > 0)
            {
                var b = tokens[k];
                if (_read.LineModes[b.Line] is not (MarkupMode.Text or MarkupMode.Tag or MarkupMode.CloseTag) || _verbatim > 0)
                {
                    _lineIndent = Leading(b.Line);   // inside a comment, a value, raw text, <pre>: as written
                    return null;
                }

                int indent = IndentFor(k);
                _lineIndent = indent;
                return _style.Break(indent, breaks > 1 ? 1 : 0);
            }

            return _verbatim > 0 ? null : SameLine(k, gap);
        }

        // The indentation (in steps) of a line whose first token is k.
        private readonly int IndentFor(int k)
        {
            var b = _tokens[k];
            int top = _open.Count - 1;
            switch (b.Kind)
            {
                case MarkupTokenKind.CloseTagOpen when k + 1 < _tokens.Count && _tokens[k + 1].Kind == MarkupTokenKind.TagName:
                    {
                        var name = Text(_tokens[k + 1]);
                        for (int i = top; i >= 0; i--)
                        {
                            if (_open[i].Kind == Open.Element && NameOf(_open[i]).Equals(name, StringComparison.OrdinalIgnoreCase))
                            {
                                return _open[i].LineIndent;
                            }

                            if (!_open[i].Optional)
                            {
                                break;
                            }
                        }

                        break;
                    }

                case MarkupTokenKind.BlockClose:
                    for (int i = top; i >= 0; i--)
                    {
                        if (_open[i].Kind == Open.Block)
                        {
                            return _open[i].LineIndent;
                        }

                        if (!_open[i].Optional)
                        {
                            break;
                        }
                    }

                    break;
                case MarkupTokenKind.TagEnd or MarkupTokenKind.SelfClose when top >= 0 && _open[top].Kind == Open.StartTag:
                case MarkupTokenKind.CloseTagEnd when top >= 0 && _open[top].Kind == Open.EndTag:
                    return _open[top].LineIndent;
                case MarkupTokenKind.TagOpen when k + 1 < _tokens.Count && _tokens[k + 1].Kind == MarkupTokenKind.TagName:
                    {
                        var name = Text(_tokens[k + 1]);
                        while (top >= 0 && _open[top] is { Kind: Open.Element, Optional: true } open && HtmlNames.Implies(name, NameOf(open)))
                        {
                            top--;
                        }

                        break;
                    }
            }

            return top < 0 ? 0 : _open[top].LineIndent + 1;
        }

        // The white space wanted between two tokens on a line; null keeps it (text keeps its spaces).
        private readonly string? SameLine(int k, ReadOnlySpan<char> gap)
        {
            var a = _tokens[k - 1].Kind;
            var b = _tokens[k].Kind;
            if (a == MarkupTokenKind.AttrEquals || b == MarkupTokenKind.AttrEquals || b is MarkupTokenKind.TagEnd or MarkupTokenKind.CloseTagEnd)
            {
                return "";
            }

            if (b == MarkupTokenKind.SelfClose)
            {
                return " ";
            }

            if (a is MarkupTokenKind.TagName or MarkupTokenKind.AttrName or MarkupTokenKind.AttrValue && b == MarkupTokenKind.AttrName)
            {
                return " ";
            }

            if ((a == MarkupTokenKind.BlockKeyword && b is MarkupTokenKind.BlockKeyword or MarkupTokenKind.BlockParams or MarkupTokenKind.BlockOpen)
                || (a == MarkupTokenKind.BlockParams && b == MarkupTokenKind.BlockOpen)
                || (a == MarkupTokenKind.BlockClose && b == MarkupTokenKind.BlockKeyword))
            {
                return " ";
            }

            return null;
        }

        // After token k: the elements, tags and blocks it opens or closes.
        private void Took(int k)
        {
            var t = _tokens[k];
            switch (t.Kind)
            {
                case MarkupTokenKind.TagOpen:
                    {
                        int nameStart = k + 1 < _tokens.Count && _tokens[k + 1].Kind == MarkupTokenKind.TagName ? _tokens[k + 1].Start : t.End;
                        int nameLength = k + 1 < _tokens.Count && _tokens[k + 1].Kind == MarkupTokenKind.TagName ? _tokens[k + 1].Length : 0;
                        var name = _text.Slice(nameStart, nameLength);
                        while (_open.Count > 0 && _open[^1] is { Kind: Open.Element, Optional: true } open && HtmlNames.Implies(name, NameOf(open)))
                        {
                            Pop();
                        }

                        _open.Add(new Entry { Kind = Open.StartTag, NameStart = nameStart, NameLength = nameLength, LineIndent = _lineIndent, At = t.Start });
                        break;
                    }

                case MarkupTokenKind.AttrValue when _open.Count > 0 && _open[^1].Kind == Open.StartTag && k > 1
                    && _tokens[k - 1].Kind == MarkupTokenKind.AttrEquals && _tokens[k - 2].Kind == MarkupTokenKind.AttrName
                    && Text(_tokens[k - 2]).Equals("type", StringComparison.OrdinalIgnoreCase):
                    {
                        var entry = _open[^1];
                        entry.Script = HtmlNames.ScriptKindOf(Text(t).Trim('"').Trim('\''));
                        _open[^1] = entry;
                        break;
                    }

                case MarkupTokenKind.TagEnd or MarkupTokenKind.SelfClose when _open.Count > 0 && _open[^1].Kind == Open.StartTag:
                    {
                        var tag = _open[^1];
                        _open.RemoveAt(_open.Count - 1);
                        var name = NameOf(tag);
                        var raw = HtmlNames.KindOf(name);
                        if (HtmlNames.Void.Contains(name) || (t.Kind == MarkupTokenKind.SelfClose && raw == ElementKind.Normal))
                        {
                            break;
                        }

                        bool verbatim = name.Equals("pre", StringComparison.OrdinalIgnoreCase) || raw == ElementKind.Textarea;
                        _open.Add(tag with { Kind = Open.Element, Optional = HtmlNames.OptionalEnd.Contains(name), Verbatim = verbatim, Raw = raw, ContentStart = t.End });
                        if (verbatim)
                        {
                            _verbatim++;
                        }

                        break;
                    }

                case MarkupTokenKind.CloseTagOpen:
                    {
                        var name = k + 1 < _tokens.Count && _tokens[k + 1].Kind == MarkupTokenKind.TagName ? Text(_tokens[k + 1]) : default;
                        while (true)
                        {
                            if (_open.Count == 0 || _open[^1].Kind is Open.Block or Open.StartTag or Open.EndTag)
                            {
                                throw ITextFormatter.Malformed(_text, t.Start, $"</{name}> closes nothing open");
                            }

                            var top = _open[^1];
                            if (NameOf(top).Equals(name, StringComparison.OrdinalIgnoreCase))
                            {
                                if (top.Raw is ElementKind.Script or ElementKind.Style)
                                {
                                    Embed(top, t.Start);
                                }

                                Pop();
                                break;
                            }

                            if (!top.Optional)
                            {
                                throw ITextFormatter.Malformed(_text, t.Start, $"</{name}> where <{NameOf(top)}> is not closed (opened at line {LineOf(top.At)})");
                            }

                            Pop();
                        }

                        _open.Add(new Entry { Kind = Open.EndTag, LineIndent = _lineIndent, At = t.Start });
                        break;
                    }

                case MarkupTokenKind.CloseTagEnd when _open.Count > 0 && _open[^1].Kind == Open.EndTag:
                    _open.RemoveAt(_open.Count - 1);
                    break;
                case MarkupTokenKind.BlockOpen:
                    _open.Add(new Entry { Kind = Open.Block, LineIndent = _lineIndent, At = t.Start });
                    break;
                case MarkupTokenKind.BlockClose:
                    while (_open.Count > 0 && _open[^1] is { Kind: Open.Element, Optional: true })
                    {
                        Pop();
                    }

                    if (_open.Count == 0 || _open[^1].Kind != Open.Block)
                    {
                        string what = _open.Count > 0 && _open[^1].Kind == Open.Element ? $"'}}' where <{NameOf(_open[^1])}> is not closed" : "'}' closes no block";
                        throw ITextFormatter.Malformed(_text, t.Start, what);
                    }

                    _open.RemoveAt(_open.Count - 1);
                    break;
            }
        }

        private void Pop()
        {
            if (_open[^1].Verbatim)
            {
                _verbatim--;
            }

            _open.RemoveAt(_open.Count - 1);
        }

        private readonly void Finish()
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                var open = _open[i];
                switch (open.Kind)
                {
                    case Open.StartTag or Open.EndTag:
                        throw ITextFormatter.Malformed(_text, open.At, "the tag is not closed");
                    case Open.Block:
                        throw ITextFormatter.Malformed(_text, open.At, "the block's '{' is not closed");
                    case Open.Element when !open.Optional:
                        throw ITextFormatter.Malformed(_text, open.At, $"<{NameOf(open)}> is not closed");
                }
            }

            (string What, MarkupTokenKind Kind)? end = _read.EndMode switch
            {
                MarkupMode.Comment => ("the comment is not closed", MarkupTokenKind.Comment),
                MarkupMode.AttrDouble or MarkupMode.AttrSingle => ("an attribute's value is not closed", MarkupTokenKind.AttrValue),
                MarkupMode.Interp => ("'{{' is not closed", MarkupTokenKind.InterpOpen),
                MarkupMode.Bogus => ("'<!' is not closed", MarkupTokenKind.Doctype),
                MarkupMode.BlockParams => ("a block's '(' is not closed", MarkupTokenKind.BlockParams),
                MarkupMode.LetExpr => ("@let's ';' is missing", MarkupTokenKind.BlockKeyword),
                _ => null,
            };
            if (end is { } unclosed)
            {
                throw ITextFormatter.Malformed(_text, StartOf(unclosed.Kind), unclosed.What);
            }
        }

        // Where the last construct of `kind` started (its first line's part, for one continued over lines).
        private readonly int StartOf(MarkupTokenKind kind)
        {
            int at = _tokens.FindLastIndex(t => t.Kind == kind);
            if (at < 0)
            {
                return _text.Length;
            }

            while (at > 0 && _tokens[at - 1].Kind == kind && _tokens[at - 1].Line == _tokens[at].Line - 1)
            {
                at--;
            }

            return _tokens[at].Start;
        }

        // A <script>'s JavaScript or a <style>'s CSS formatted one step inside its element (as written when it does not parse).
        private readonly void Embed(Entry element, int end)
        {
            if (!_embedded)
            {
                return;
            }

            ITextFormatter? formatter = element.Raw == ElementKind.Style
                ? new StyleFormatter(StyleDialect.Css)
                : element.Script is ScriptKind.JavaScript or ScriptKind.Json ? new ScriptFormatter(ScriptTokenizer.For(ScriptDialect.JavaScript, ScriptVersion.Newest)) : null;
            int start = element.ContentStart;
            var content = _text[start..end];
            if (formatter is null || !content.ContainsAny('\r', '\n'))
            {
                return;
            }

            int first = content.IndexOfAnyExcept(Blank);
            int last = content.LastIndexOfAnyExcept(Blank);
            string inside = _style.Break(element.LineIndent + 1);
            string closing = _style.Break(element.LineIndent);
            if (first < 0)
            {
                Add(start, content.Length, closing);
                return;
            }

            var inner = new FormatStyle(_style.Indent, inside);
            var edits = new List<TextEdit>();
            try
            {
                formatter.Format(content, inner, edits);
            }
            catch (FormatException)
            {
                return;   // the script or style does not parse: kept as written
            }

            string blank = inside + inside;
            string blankWanted = _style.NewLine + inside;
            foreach (var edit in edits)
            {
                if (edit.Start < first || edit.End > last + 1)
                {
                    continue;
                }

                Add(start + edit.Start, edit.Length, edit.Text.Replace(blank, blankWanted, StringComparison.Ordinal));
            }

            Add(start, first, Breaks(content[..first]) > 0 ? inside : "");
            Add(start + last + 1, content.Length - last - 1, Breaks(content[(last + 1)..]) > 0 ? closing : "");
        }

        private readonly void Add(int start, int length, string text)
        {
            if (!_text.Slice(start, length).SequenceEqual(text))
            {
                _edits.Add(new TextEdit(start, length, text));
            }
        }

        private readonly int LineOf(int at)
        {
            int line = 1;
            for (int i = 0; i < at && i < _text.Length; i++)
            {
                if (_text[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }

        private static int Breaks(ReadOnlySpan<char> gap)
        {
            int count = 0;
            for (int i = 0; i < gap.Length; i++)
            {
                if (gap[i] == '\n' || (gap[i] == '\r' && (i + 1 >= gap.Length || gap[i + 1] != '\n')))
                {
                    count++;
                }
            }

            return count;
        }

        private readonly int Leading(int line)
        {
            int at = _read.LineStarts[line];
            int columns = 0;
            while (at < _text.Length && _text[at] is ' ' or '\t')
            {
                columns += _text[at] == '\t' ? 4 : 1;
                at++;
            }

            int step = _style.Indent == "\t" ? 4 : Math.Max(1, _style.Indent.Length);
            return columns / step;
        }
    }
}
