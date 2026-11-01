// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Web.Script;

/// <summary>
/// The built-in formatter of JavaScript, TypeScript, JSX and TSX (W-script): only the white space between tokens changes,
/// so the caret keeps its place and no character of the code does. Each line is indented by the brackets (<c>{}</c>,
/// <c>()</c>, <c>[]</c>, <c>${}</c>) and the JSX elements open at its start (brackets opened on one line indent the next by
/// one step), a line continuing an expression (after an operator, before a <c>.</c> or an operator, the body of an
/// <c>if</c> without braces) one step more; inside a line one space after a keyword and around a binary operator, none
/// inside <c>()</c> and <c>[]</c>, one inside <c>{ }</c>, one after <c>,</c> and <c>;</c>, runs of spaces made one; at most
/// one empty line kept, trailing spaces taken off. A template literal's text, a string continued across lines and JSX text
/// are kept as written. No line is broken unless a line width is set (<see cref="LineWidth"/>), then after a comma where
/// the next part would pass it. A malformed text (a bracket, a template, a comment or a string not closed, a JSX closing
/// tag that does not match) is left as it is, the message naming the line and the column.
/// </summary>
public sealed class ScriptFormatter : ITextFormatter
{
    private static readonly System.Buffers.SearchValues<char> Blank = System.Buffers.SearchValues.Create(" \t\r\n");
    private readonly Func<int>? _lineWidth;
    private ScriptTokenizer _tokenizer;

    /// <summary>A formatter that reads the text with <paramref name="tokenizer"/> (its dialect: JSX or not), breaking lines past <paramref name="lineWidth"/>'s width when it gives more than 0.</summary>
    public ScriptFormatter(ScriptTokenizer tokenizer, Func<int>? lineWidth = null)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        _tokenizer = tokenizer;
        _lineWidth = lineWidth;
    }

    /// <summary>The lexer it reads with (a session's dialect: changed with the session's when a project's version is learned).</summary>
    public ScriptTokenizer Tokenizer
    {
        get => Volatile.Read(ref _tokenizer);
        set => Volatile.Write(ref _tokenizer, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>The line width past which a line is broken after a comma; 0 (the default) breaks none.</summary>
    public int LineWidth => Math.Max(0, _lineWidth?.Invoke() ?? 0);

    /// <inheritdoc />
    public void Format(ReadOnlySpan<char> text, FormatStyle style, List<TextEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(edits);
        var tokenizer = Tokenizer;
        var read = Read(tokenizer, text);
        Check(text, read);
        new Layout(text, style, read, LineWidth, edits).Run();
    }

    // ---- reading ----

    internal readonly record struct Tok(int Start, int End, ScriptTokenKind Kind, FrameKind Frame, int Line)
    {
        public int Length => End - Start;
    }

    internal sealed class Collector : IScriptTokenSink
    {
        public List<Tok> Tokens { get; } = [];

        public int Line { get; set; }

        public void Token(int start, int length, ScriptTokenKind kind, FrameKind frame) => Tokens.Add(new Tok(start, start + length, kind, frame, Line));
    }

    internal sealed record Reading(List<Tok> Tokens, FrameKind[] LineKinds, Sub[] LineSubs, int[] LineStarts, ulong EndState, Sub EndSub, bool EndNested);

    // Lexes every line with the sink: the tokens, each line's start (its frame and what continues into it), the end state.
    internal static Reading Read(ScriptTokenizer tokenizer, ReadOnlySpan<char> text)
    {
        var collector = new Collector();
        var kinds = new List<FrameKind>();
        var subs = new List<Sub>();
        var starts = new List<int>();
        ulong state = tokenizer.Initial;
        int start = 0;
        int line = 0;
        while (true)
        {
            int length = text[start..].IndexOfAny('\r', '\n');
            int end = length < 0 ? text.Length : start + length;
            kinds.Add(tokenizer.TopOf(state, out var sub));
            subs.Add(sub);
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

        var endKind = tokenizer.TopOf(state, out var endSub);
        return new Reading(collector.Tokens, [.. kinds], [.. subs], [.. starts], state, endSub, LineStates.IsInterned(state) || endKind != FrameKind.Code);
    }

    private enum Open : byte
    {
        Paren,
        Bracket,
        Brace,
        TemplateExpr,
        JsxExpr,
        Template,
        Tag,
        Children,
        CloseTag,
    }

    // The brackets, templates and elements balance; strings, regular expressions and comments close; a closing tag names its element.
    private static void Check(ReadOnlySpan<char> text, Reading read)
    {
        var tokens = read.Tokens;
        var open = new List<(Open Kind, int At, int NameStart, int NameLength)>();
        int comment = -1;
        for (int k = 0; k < tokens.Count; k++)
        {
            var t = tokens[k];
            switch (t.Kind)
            {
                case ScriptTokenKind.StringUnterminated:
                    throw ITextFormatter.Malformed(text, t.Start, "the string is not closed on its line");
                case ScriptTokenKind.RegexUnterminated:
                    throw ITextFormatter.Malformed(text, t.Start, "the regular expression is not closed on its line");
                case ScriptTokenKind.BlockComment:
                    if (text[t.Start..].StartsWith("/*"))
                    {
                        comment = t.Start;
                    }

                    if (t.Length >= 2 && text[(t.End - 2)..t.End].SequenceEqual("*/") && !(t.Start == comment && t.Length < 4))
                    {
                        comment = -1;
                    }

                    break;
                case ScriptTokenKind.Punct:
                    switch (text[t.Start])
                    {
                        case '(':
                            open.Add((Open.Paren, t.Start, 0, 0));
                            break;
                        case '[':
                            open.Add((Open.Bracket, t.Start, 0, 0));
                            break;
                        case '{':
                            open.Add((Open.Brace, t.Start, 0, 0));
                            break;
                        case ')':
                            Close(text, open, Open.Paren, t.Start, "')'");
                            break;
                        case ']':
                            Close(text, open, Open.Bracket, t.Start, "']'");
                            break;
                        case '}':
                            Close(text, open, Open.Brace, t.Start, "'}'");
                            break;
                    }

                    break;
                case ScriptTokenKind.TemplateStart:
                    open.Add((Open.Template, t.Start, 0, 0));
                    break;
                case ScriptTokenKind.TemplateEnd:
                    Close(text, open, Open.Template, t.Start, "'`'");
                    break;
                case ScriptTokenKind.TemplateExprOpen:
                    open.Add((Open.TemplateExpr, t.Start, 0, 0));
                    break;
                case ScriptTokenKind.TemplateExprClose:
                    Close(text, open, Open.TemplateExpr, t.Start, "'}'");
                    break;
                case ScriptTokenKind.JsxExprOpen:
                    open.Add((Open.JsxExpr, t.Start, 0, 0));
                    break;
                case ScriptTokenKind.JsxExprClose:
                    Close(text, open, Open.JsxExpr, t.Start, "'}'");
                    break;
                case ScriptTokenKind.JsxTagOpen:
                    open.Add((Open.Tag, t.Start, t.End, 0));
                    break;
                case ScriptTokenKind.JsxTagName when open.Count > 0 && open[^1].Kind == Open.Tag:
                    open[^1] = (Open.Tag, open[^1].At, t.Start, t.Length);
                    break;
                case ScriptTokenKind.JsxTagName when open.Count > 0 && open[^1].Kind == Open.CloseTag:
                    {
                        var element = open[^1];
                        if (!text.Slice(element.NameStart, element.NameLength).SequenceEqual(text.Slice(t.Start, t.Length)))
                        {
                            throw ITextFormatter.Malformed(text, t.Start, $"</{text.Slice(t.Start, t.Length)}> does not close <{text.Slice(element.NameStart, element.NameLength)}>");
                        }

                        open[^1] = (Open.CloseTag, element.At, element.NameStart, -1);   // named: matched
                        break;
                    }

                case ScriptTokenKind.JsxTagEnd:
                    {
                        var tag = Close(text, open, Open.Tag, t.Start, "'>'");
                        open.Add((Open.Children, tag.At, tag.NameStart, tag.NameLength));
                        break;
                    }

                case ScriptTokenKind.JsxSelfClose:
                    Close(text, open, Open.Tag, t.Start, "'/>'");
                    break;
                case ScriptTokenKind.JsxCloseTagOpen:
                    {
                        var children = Close(text, open, Open.Children, t.Start, "'</'");
                        open.Add((Open.CloseTag, children.At, children.NameStart, children.NameLength));
                        break;
                    }

                case ScriptTokenKind.JsxCloseTagEnd:
                    {
                        var closing = Close(text, open, Open.CloseTag, t.Start, "'>'");
                        if (closing.NameLength > 0)
                        {
                            throw ITextFormatter.Malformed(text, t.Start, $"</> does not close <{text.Slice(closing.NameStart, closing.NameLength)}>");
                        }

                        break;
                    }
            }
        }

        if (read.EndSub is Sub.Block or Sub.Doc)
        {
            throw ITextFormatter.Malformed(text, Math.Max(0, comment), "the comment is not closed");
        }

        if (read.EndSub is Sub.Double or Sub.Single)
        {
            throw ITextFormatter.Malformed(text, text.Length, "the string is not closed");
        }

        if (open.Count > 0)
        {
            var last = open[^1];
            string what = last.Kind switch
            {
                Open.Paren => "'(' is not closed",
                Open.Bracket => "'[' is not closed",
                Open.Brace => "'{' is not closed",
                Open.Template => "the template literal is not closed",
                Open.TemplateExpr => "'${' is not closed",
                Open.JsxExpr => "the JSX expression's '{' is not closed",
                Open.Tag => "the JSX tag is not closed",
                Open.Children => $"<{text.Slice(last.NameStart, last.NameLength)}> is not closed",
                _ => "the JSX closing tag is not closed",
            };
            throw ITextFormatter.Malformed(text, last.At, what);
        }
    }

    private static (Open Kind, int At, int NameStart, int NameLength) Close(ReadOnlySpan<char> text, List<(Open Kind, int At, int NameStart, int NameLength)> open, Open kind, int at, string what)
    {
        if (open.Count == 0)
        {
            throw ITextFormatter.Malformed(text, at, $"{what} closes nothing");
        }

        var top = open[^1];
        if (top.Kind != kind)
        {
            throw ITextFormatter.Malformed(text, at, $"{what} where {Expected(top.Kind)} was expected (opened at line {LineOf(text, top.At)})");
        }

        open.RemoveAt(open.Count - 1);
        return top;
    }

    private static string Expected(Open kind) => kind switch
    {
        Open.Paren => "')'",
        Open.Bracket => "']'",
        Open.Brace or Open.TemplateExpr or Open.JsxExpr => "'}'",
        Open.Template => "'`'",
        Open.Tag => "'>' or '/>'",
        Open.Children => "a closing tag",
        _ => "'>'",
    };

    private static int LineOf(ReadOnlySpan<char> text, int at)
    {
        int line = 1;
        for (int i = 0; i < at && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    // ---- laying out ----

    private ref struct Layout
    {
        private const string Space = " ";
        private readonly ReadOnlySpan<char> _text;
        private readonly FormatStyle _style;
        private readonly Reading _read;
        private readonly List<Tok> _tokens;
        private readonly int _width;
        private readonly List<TextEdit> _edits;
        private readonly bool[] _values;
        private readonly List<(Open Kind, int LineIndent, bool Control)> _open = [];
        private int _lineIndent;
        private int _column;
        private int _lastCode = -1;
        private bool _lastControlClose;
        private int _commentIndent;

        public Layout(ReadOnlySpan<char> text, FormatStyle style, Reading read, int width, List<TextEdit> edits)
        {
            _text = text;
            _style = style;
            _read = read;
            _tokens = read.Tokens;
            _width = width;
            _edits = edits;
            _values = new bool[_tokens.Count];
        }

        private readonly int StepWidth => _style.Indent == "\t" ? 4 : _style.Indent.Length;

        public void Run()
        {
            var tokens = _tokens;
            for (int k = 0; k < tokens.Count; k++)
            {
                _values[k] = IsValue(k);
            }

            for (int k = 0; k <= tokens.Count; k++)
            {
                int gapStart = k == 0 ? 0 : tokens[k - 1].End;
                int gapEnd = k == tokens.Count ? _text.Length : tokens[k].Start;
                var gap = _text[gapStart..gapEnd];
                string? wanted = Gap(k, gap);
                if (wanted is not null && gap.ContainsAnyExcept(Blank))
                {
                    wanted = null;   // a gap holding other white space (a BOM, a no-break space) is kept as it is
                }

                if (wanted is not null && !gap.SequenceEqual(wanted))
                {
                    _edits.Add(new TextEdit(gapStart, gapEnd - gapStart, wanted));
                }

                Advance(wanted is null ? gap : wanted);
                if (k < tokens.Count)
                {
                    Took(k);
                }
            }
        }

        private void Advance(ReadOnlySpan<char> written)
        {
            int newline = written.LastIndexOfAny('\r', '\n');
            if (newline < 0)
            {
                _column += written.Length;
                return;
            }

            _column = 0;
            foreach (char c in written[(newline + 1)..])
            {
                _column += c == '\t' ? 4 : 1;
            }
        }

        // The white space wanted before token k (k == count: after the last); null keeps the gap as it is.
        private string? Gap(int k, ReadOnlySpan<char> gap)
        {
            var tokens = _tokens;
            if (k == tokens.Count)
            {
                return k == 0 ? null : gap.ContainsAny('\r', '\n') ? _style.NewLine : "";
            }

            var b = tokens[k];
            int breaks = Breaks(gap);
            if (k == 0)
            {
                _lineIndent = 0;
                return "";   // nothing before the first token
            }

            if (breaks > 0)
            {
                return NewLine(k, gap, breaks);
            }

            var a = tokens[k - 1];
            if (_width > 0 && a.Kind == ScriptTokenKind.Punct && _text[a.Start] == ',' && Code(b) && !IsComment(b))
            {
                string? same = SameLine(k, gap);
                int segment = Segment(k);
                if (_column + (same?.Length ?? gap.Length) + segment > _width)
                {
                    int indent = IndentFor(k);
                    _lineIndent = indent;
                    return _style.Break(indent);
                }

                return same;
            }

            return SameLine(k, gap);
        }

        private string? NewLine(int k, ReadOnlySpan<char> gap, int breaks)
        {
            var b = _tokens[k];
            var lineKind = _read.LineKinds[b.Line];
            var lineSub = _read.LineSubs[b.Line];
            if (b.Line > 0 && (lineKind == FrameKind.Template || lineSub is Sub.Double or Sub.Single))
            {
                _lineIndent = Leading(b.Line);   // a template's or a string's text: as written
                return null;
            }

            if (lineSub is Sub.Block or Sub.Doc && b.Kind == ScriptTokenKind.BlockComment)
            {
                if (_text[b.Start] == '*')
                {
                    _lineIndent = _commentIndent;
                    return _style.Break(_commentIndent, breaks > 1 ? 1 : 0) + Space;   // a comment's " * " lines under its "/**"
                }

                _lineIndent = Leading(b.Line);
                return null;
            }

            int indent = IndentFor(k);
            _lineIndent = indent;
            return _style.Break(indent, breaks > 1 ? 1 : 0);
        }

        // The indentation (in steps) of a line whose first token is k.
        private readonly int IndentFor(int k)
        {
            var b = _tokens[k];
            if (_open.Count > 0 && Closes(b, _open[^1].Kind))
            {
                return _open[^1].LineIndent;
            }

            int indent = _open.Count == 0 ? 0 : _open[^1].LineIndent + 1;
            return Continues(k) ? indent + 1 : indent;
        }

        private readonly bool Closes(Tok b, Open top) => b.Kind switch
        {
            ScriptTokenKind.Punct => (_text[b.Start], top) is (')', Open.Paren) or (']', Open.Bracket) or ('}', Open.Brace),
            ScriptTokenKind.TemplateExprClose => top == Open.TemplateExpr,
            ScriptTokenKind.JsxExprClose => top == Open.JsxExpr,
            ScriptTokenKind.JsxTagEnd or ScriptTokenKind.JsxSelfClose => top == Open.Tag,
            ScriptTokenKind.JsxCloseTagOpen => top == Open.Children,
            ScriptTokenKind.JsxCloseTagEnd => top == Open.CloseTag,
            _ => false,
        };

        // A line that continues the expression above: after an operator, a '.', an if's ')' or else; or starting with '.' or an operator.
        private readonly bool Continues(int k)
        {
            var b = _tokens[k];
            if (_open.Count > 0 && _open[^1].Kind is not (Open.Brace or Open.TemplateExpr or Open.JsxExpr))
            {
                return false;
            }

            if (b.Frame is not (FrameKind.Code or FrameKind.TemplateExpr or FrameKind.JsxExpr) || (b.Kind == ScriptTokenKind.Punct && _text[b.Start] == '{'))
            {
                return false;
            }

            if (b.Kind == ScriptTokenKind.Punct && _text[b.Start] == '.')
            {
                return true;
            }

            if (b.Kind == ScriptTokenKind.Operator && Text(b) is var op && !op.SequenceEqual("++") && !op.SequenceEqual("--") && !op.SequenceEqual("!") && !op.SequenceEqual("~") && !op.SequenceEqual("..."))
            {
                return true;
            }

            if (_lastCode < 0)
            {
                return false;
            }

            var last = _tokens[_lastCode];
            if (last.Kind == ScriptTokenKind.Operator)
            {
                var text = Text(last);
                return !text.SequenceEqual("++") && !text.SequenceEqual("--") && !(text.SequenceEqual("!") && _values[_lastCode]);
            }

            if (last.Kind == ScriptTokenKind.Punct && _text[last.Start] == '.')
            {
                return true;
            }

            if (_lastControlClose)
            {
                return true;
            }

            return last.Kind == ScriptTokenKind.Keyword && (Text(last).SequenceEqual("else") || Text(last).SequenceEqual("do"))
                && !(b.Kind == ScriptTokenKind.Keyword && Text(b).SequenceEqual("if"));
        }

        // After token k: the brackets and elements it opens or closes, the last code token.
        private void Took(int k)
        {
            var t = _tokens[k];
            _column += t.Length;
            switch (t.Kind)
            {
                case ScriptTokenKind.Punct:
                    switch (_text[t.Start])
                    {
                        case '(':
                            bool control = k > 0 && _tokens[k - 1].Kind == ScriptTokenKind.Keyword && Text(_tokens[k - 1]) is var keyword
                                && (keyword.SequenceEqual("if") || keyword.SequenceEqual("for") || keyword.SequenceEqual("while") || keyword.SequenceEqual("with"));
                            _open.Add((Open.Paren, _lineIndent, control));
                            break;
                        case '[':
                            _open.Add((Open.Bracket, _lineIndent, false));
                            break;
                        case '{':
                            _open.Add((Open.Brace, _lineIndent, false));
                            break;
                        case ')' or ']' or '}':
                            bool wasControl = PopOpen().Control;
                            _lastCode = k;
                            _lastControlClose = wasControl;
                            return;
                    }

                    break;
                case ScriptTokenKind.TemplateExprOpen:
                    _open.Add((Open.TemplateExpr, _lineIndent, false));
                    break;
                case ScriptTokenKind.JsxExprOpen:
                    _open.Add((Open.JsxExpr, _lineIndent, false));
                    break;
                case ScriptTokenKind.JsxTagOpen:
                    _open.Add((Open.Tag, _lineIndent, false));
                    break;
                case ScriptTokenKind.JsxTagEnd:
                    var tag = PopOpen();
                    _open.Add((Open.Children, tag.LineIndent, false));   // children one step in from the tag's '<' line
                    break;
                case ScriptTokenKind.JsxCloseTagOpen:
                    PopOpen();
                    _open.Add((Open.CloseTag, _lineIndent, false));
                    break;
                case ScriptTokenKind.TemplateExprClose or ScriptTokenKind.JsxExprClose or ScriptTokenKind.JsxSelfClose or ScriptTokenKind.JsxCloseTagEnd:
                    PopOpen();
                    break;
                case ScriptTokenKind.BlockComment when _text[t.Start..].StartsWith("/*"):
                    _commentIndent = _lineIndent;
                    return;
                case ScriptTokenKind.BlockComment or ScriptTokenKind.LineComment:
                    return;
            }

            if (t.Kind is not (ScriptTokenKind.TemplateString or ScriptTokenKind.JsxText))
            {
                _lastCode = k;
                _lastControlClose = false;
            }
        }

        private (Open Kind, int LineIndent, bool Control) PopOpen()
        {
            if (_open.Count == 0)
            {
                return default;
            }

            var top = _open[^1];
            _open.RemoveAt(_open.Count - 1);
            return top;
        }

        // The width the tokens from k take on their line up to the next comma at their depth (with it), or the line's end.
        private readonly int Segment(int k)
        {
            var tokens = _tokens;
            int width = tokens[k].Length;
            int depth = 0;
            for (int j = k + 1; j < tokens.Count && tokens[j].Line == tokens[k].Line; j++)
            {
                var t = tokens[j];
                if (t.Kind == ScriptTokenKind.Punct)
                {
                    char c = _text[t.Start];
                    if (c is '(' or '[' or '{')
                    {
                        depth++;
                    }
                    else if (c is ')' or ']' or '}')
                    {
                        if (depth == 0)
                        {
                            break;
                        }

                        depth--;
                    }
                    else if (c == ',' && depth == 0)
                    {
                        return width + 1;
                    }
                }

                var gap = _text[tokens[j - 1].End..t.Start];
                width += (SameLine(j, gap)?.Length ?? gap.Length) + t.Length;
            }

            return width;
        }

        // The white space wanted between two tokens on one line; null keeps it.
        private readonly string? SameLine(int k, ReadOnlySpan<char> gap)
        {
            var a = _tokens[k - 1];
            var b = _tokens[k];
            string keep = gap.Length == 0 ? "" : Space;

            // JSX: children as written; in a tag, no space around '=' or before '>', one before '/>', runs made one.
            if (b.Frame == FrameKind.JsxChildren || a.Kind == ScriptTokenKind.JsxText || b.Kind == ScriptTokenKind.JsxText)
            {
                return null;
            }

            if (a.Kind == ScriptTokenKind.JsxAttrEquals || b.Kind == ScriptTokenKind.JsxAttrEquals || b.Kind is ScriptTokenKind.JsxTagEnd or ScriptTokenKind.JsxCloseTagEnd
                || a.Kind is ScriptTokenKind.JsxTagOpen or ScriptTokenKind.JsxCloseTagOpen)
            {
                return "";
            }

            if (b.Kind == ScriptTokenKind.JsxSelfClose)
            {
                return Space;
            }

            if (b.Frame is FrameKind.JsxOpenTag or FrameKind.JsxCloseTag || a.Kind is ScriptTokenKind.JsxTagName or ScriptTokenKind.JsxAttrName or ScriptTokenKind.JsxAttrString)
            {
                return keep;
            }

            if (IsComment(a) || IsComment(b))
            {
                return keep;
            }

            if (a.Kind is ScriptTokenKind.TemplateExprOpen or ScriptTokenKind.JsxExprOpen || b.Kind is ScriptTokenKind.TemplateExprClose or ScriptTokenKind.JsxExprClose)
            {
                return "";
            }

            if (b.Kind == ScriptTokenKind.TemplateStart && a.Kind is ScriptTokenKind.Word or ScriptTokenKind.TemplateEnd or ScriptTokenKind.Punct)
            {
                return keep;   // a tagged template (html`...`), as written
            }

            char ac = a.Kind == ScriptTokenKind.Punct ? _text[a.Start] : '\0';
            char bc = b.Kind == ScriptTokenKind.Punct ? _text[b.Start] : '\0';
            var ao = a.Kind == ScriptTokenKind.Operator ? Text(a) : default;
            var bo = b.Kind == ScriptTokenKind.Operator ? Text(b) : default;

            if (bc is ',' or ';' or ')' or ']')
            {
                return "";
            }

            if (ac is '(' or '[')
            {
                return "";
            }

            if (ac == '.' || bc == '.')
            {
                return a.Kind == ScriptTokenKind.Number && bc == '.' ? keep : "";
            }

            if (ao.SequenceEqual("?.") || bo.SequenceEqual("?.") || ao.SequenceEqual("..."))
            {
                return "";
            }

            if (ac is ',' or ';')
            {
                return b.Kind == ScriptTokenKind.Operator && Category(bo) == Op.Keep ? keep : Space;
            }

            if (ac == '{' && bc == '}')
            {
                return "";
            }

            if (ac == '{' || bc == '}' || bc == '{')
            {
                return Space;
            }

            if (b.Kind == ScriptTokenKind.Operator)
            {
                return Before(k, a, bo, keep);
            }

            if (a.Kind == ScriptTokenKind.Operator)
            {
                return After(k - 1, ao, b, keep);
            }

            if (bc == '(')
            {
                if (a.Kind == ScriptTokenKind.Keyword)
                {
                    var keyword = Text(a);
                    return keyword.SequenceEqual("if") || keyword.SequenceEqual("for") || keyword.SequenceEqual("while") || keyword.SequenceEqual("switch")
                        || keyword.SequenceEqual("catch") || keyword.SequenceEqual("with") ? Space : keep;
                }

                return a.Kind is ScriptTokenKind.Word or ScriptTokenKind.PrivateName || ac is ')' or ']' ? "" : keep;
            }

            if (bc == '[')
            {
                return a.Kind is ScriptTokenKind.Word or ScriptTokenKind.PrivateName || ac is ')' or ']' ? "" : keep;
            }

            if (ac is '}' or ')' or ']' && b.Kind is ScriptTokenKind.Word or ScriptTokenKind.Keyword or ScriptTokenKind.Number or ScriptTokenKind.String)
            {
                return Space;
            }

            if (WordLike(a) && WordLike(b))
            {
                return Space;
            }

            if (a.Kind == ScriptTokenKind.Keyword && !_values[k - 1] && b.Kind != ScriptTokenKind.Punct)
            {
                return Space;
            }

            return keep;
        }

        private enum Op : byte
        {
            Spaced,     // a binary operator, an assignment, '=>'
            PlusMinus,  // binary after a value, else unary
            Prefix,     // '!' '~': unary (TS's postfix '!' after a value)
            Step,       // '++' '--'
            Star,       // binary after a value, else function*, yield*, import *
            Colon,      // as written before, one space after
            Keep,       // '<' '>' '>>' '>>>' '?': generics or comparisons, as written
        }

        private static Op Category(ReadOnlySpan<char> op) => op switch
        {
            "+" or "-" => Op.PlusMinus,
            "!" or "~" => Op.Prefix,
            "++" or "--" => Op.Step,
            "*" => Op.Star,
            ":" => Op.Colon,
            "<" or ">" or ">>" or ">>>" or "?" => Op.Keep,
            _ => Op.Spaced,
        };

        // Before operator b (token k), after token a.
        private readonly string Before(int k, Tok a, ReadOnlySpan<char> op, string keep)
        {
            bool value = _values[k - 1];
            var category = Category(op);
            if (a.Kind == ScriptTokenKind.Operator && category is Op.PlusMinus or Op.Prefix or Op.Step && !value)
            {
                // a chain of prefix operators: together, unless joining them would make another token (- -x, + +x)
                var ao = Text(a);
                bool unaryBefore = Category(ao) is Op.PlusMinus or Op.Prefix or Op.Step && !(k >= 2 && _values[k - 2]);
                if (unaryBefore)
                {
                    return ao[^1] == op[0] ? keep : "";
                }
            }

            return category switch
            {
                Op.Spaced => Space,
                Op.PlusMinus => value ? Space : a.Kind is ScriptTokenKind.Keyword or ScriptTokenKind.Operator ? Space : keep,
                Op.Prefix or Op.Step => value ? "" : a.Kind is ScriptTokenKind.Keyword or ScriptTokenKind.Operator ? Space : keep,
                Op.Star => value ? Space : keep,
                _ => keep,
            };
        }

        // After operator a (token k), before token b.
        private readonly string After(int k, ReadOnlySpan<char> op, Tok b, string keep)
        {
            bool binary = k > 0 && _values[k - 1];
            return Category(op) switch
            {
                Op.Spaced => Space,
                Op.Colon => Space,
                Op.PlusMinus => binary ? Space : "",
                Op.Prefix or Op.Step => binary ? keep : "",
                Op.Star => binary ? Space : keep,
                _ => keep,
            };
        }

        private readonly bool IsValue(int k)
        {
            var t = _tokens[k];
            switch (t.Kind)
            {
                case ScriptTokenKind.Word or ScriptTokenKind.PrivateName or ScriptTokenKind.Number or ScriptTokenKind.String or ScriptTokenKind.Regex
                    or ScriptTokenKind.TemplateEnd or ScriptTokenKind.JsxSelfClose or ScriptTokenKind.JsxCloseTagEnd:
                    return true;
                case ScriptTokenKind.Keyword:
                    return ScriptWords.ValueLookup.Contains(Text(t));
                case ScriptTokenKind.Punct:
                    return _text[t.Start] is ')' or ']' or '}';
                case ScriptTokenKind.Operator:
                    var op = Text(t);
                    return (op.SequenceEqual("!") || op.SequenceEqual("++") || op.SequenceEqual("--")) && k > 0 && _values[k - 1];
                default:
                    return false;
            }
        }

        private readonly ReadOnlySpan<char> Text(Tok t) => _text.Slice(t.Start, t.Length);

        private static bool IsComment(Tok t) => t.Kind is ScriptTokenKind.LineComment or ScriptTokenKind.BlockComment;

        private static bool WordLike(Tok t) => t.Kind is ScriptTokenKind.Word or ScriptTokenKind.Keyword or ScriptTokenKind.Number or ScriptTokenKind.PrivateName
            or ScriptTokenKind.Decorator or ScriptTokenKind.String or ScriptTokenKind.Regex;

        private static bool Code(Tok t) => t.Frame is FrameKind.Code or FrameKind.TemplateExpr or FrameKind.JsxExpr;

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

        // A line's own indentation, in steps (a line kept as written).
        private readonly int Leading(int line)
        {
            int at = _read.LineStarts[line];
            int columns = 0;
            while (at < _text.Length && _text[at] is ' ' or '\t')
            {
                columns += _text[at] == '\t' ? 4 : 1;
                at++;
            }

            return columns / Math.Max(1, StepWidth);
        }
    }
}
