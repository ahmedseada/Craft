// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Web.Script;

/// <summary>
/// The folding ranges of JavaScript, TypeScript, JSX and TSX (W-script), read by the lexer in one pass over the lines
/// (strings, templates, regular expressions and comments skipped): every <c>{}</c>, <c>[]</c> and <c>()</c> over several
/// lines, JSX elements over several lines (from the <c>&lt;</c> to the closing tag), template literals over several
/// lines, <c>//#region</c> or <c>/* #region */</c> to <c>#endregion</c> (marked), block comments and runs of line
/// comments (comments), and a run of import statements (imports).
/// </summary>
public sealed class ScriptFolding : IFoldingRangeProvider
{
    private readonly Func<string, ScriptTokenizer> _tokenizer;

    /// <summary>A provider named <paramref name="name"/> for <paramref name="languages"/>, lexing with what <paramref name="tokenizer"/> gives for a language name when the request's session is not a line-state one.</summary>
    public ScriptFolding(string name, IReadOnlyList<string> languages, Func<string, ScriptTokenizer> tokenizer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(languages);
        ArgumentNullException.ThrowIfNull(tokenizer);
        Name = name;
        Languages = languages;
        _tokenizer = tokenizer;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Summary => "JavaScript and TypeScript: braces, brackets, JSX elements, templates, #region markers, comments and imports over several lines";

    /// <inheritdoc />
    public IReadOnlyList<string> Languages { get; }

    /// <inheritdoc />
    public Task<IReadOnlyList<FoldingRange>> GetRangesAsync(FoldingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tokenizer = request.Session is LineStateSession { Tokenizer: ScriptTokenizer own } ? own : _tokenizer(request.Language);
        return Task.FromResult<IReadOnlyList<FoldingRange>>(Ranges(tokenizer, request.Snapshot, cancellationToken));
    }

    /// <summary>The ranges of <paramref name="snapshot"/> read with <paramref name="tokenizer"/>.</summary>
    public static List<FoldingRange> Ranges(ScriptTokenizer tokenizer, ITextSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(snapshot);
        var sink = new Sink();
        char[] buffer = ArrayPool<char>.Shared.Rent(256);
        try
        {
            ulong state = tokenizer.Initial;
            for (int index = 0; index < snapshot.LineCount; index++)
            {
                if ((index & 1023) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var line = snapshot.GetLine(index);
                if (buffer.Length < line.Length)
                {
                    ArrayPool<char>.Shared.Return(buffer);
                    buffer = ArrayPool<char>.Shared.Rent(line.Length);
                }

                var text = buffer.AsSpan(0, line.Length);
                snapshot.CopyTo(line.Start, text);
                sink.Line = index;
                sink.Text = buffer;
                sink.LineStart = line.Start;
                state = tokenizer.Lex(text, line.Start, state, null, sink);
                sink.EndLine();
            }

            sink.Finish();
            return sink.Ranges;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private sealed class Sink : IScriptTokenSink
    {
        private readonly List<(ScriptTokenKind Kind, int Line)> _open = [];
        private readonly List<int> _regions = [];
        private int _commentStart = -1;
        private int _runStart = -1;
        private int _runEnd = -1;
        private int _importStart = -1;
        private int _importEnd = -1;
        private bool _inImport;
        private bool _lastWasModule;
        private int _tokensOnLine;
        private bool _commentOnly = true;
        private bool _pendingImport;

        public List<FoldingRange> Ranges { get; } = [];

        public int Line { get; set; }

        public char[] Text { get; set; } = [];

        public int LineStart { get; set; }

        public void Token(int start, int length, ScriptTokenKind kind, FrameKind frame)
        {
            var text = Text.AsSpan(start - LineStart, length);
            bool first = _tokensOnLine++ == 0;
            if (_pendingImport)
            {
                _pendingImport = false;
                if (kind == ScriptTokenKind.Punct && text[0] is '(' or '.')
                {
                    _inImport = false;   // import(...) or import.meta: not a statement
                    if (_importStart == Line && _importEnd < Line)
                    {
                        _importStart = -1;
                    }
                }
            }

            if (kind is not (ScriptTokenKind.LineComment or ScriptTokenKind.BlockComment))
            {
                _commentOnly = false;
            }

            if (first && kind is not (ScriptTokenKind.LineComment or ScriptTokenKind.BlockComment))
            {
                Statement(kind, text);
            }

            switch (kind)
            {
                case ScriptTokenKind.LineComment:
                    LineComment(text, first);
                    return;
                case ScriptTokenKind.BlockComment:
                    BlockComment(text);
                    return;
                case ScriptTokenKind.Punct:
                    switch (text[0])
                    {
                        case '{' or '[' or '(':
                            _open.Add((kind, Line));
                            break;
                        case '}' or ']' or ')':
                            Close();
                            break;
                        case ';' when _open.Count == 0 && _inImport:
                            _inImport = false;
                            _importEnd = Line;
                            break;
                    }

                    break;
                case ScriptTokenKind.TemplateStart or ScriptTokenKind.TemplateExprOpen or ScriptTokenKind.JsxExprOpen or ScriptTokenKind.JsxTagOpen:
                    _open.Add((kind, Line));
                    break;
                case ScriptTokenKind.TemplateEnd or ScriptTokenKind.TemplateExprClose or ScriptTokenKind.JsxExprClose or ScriptTokenKind.JsxSelfClose or ScriptTokenKind.JsxCloseTagEnd:
                    Close();
                    break;
                case ScriptTokenKind.Keyword when text.SequenceEqual("import") && first && _open.Count == 0:
                    _pendingImport = true;
                    break;
            }

            if (_inImport)
            {
                _importEnd = Line;
            }

            _lastWasModule = kind == ScriptTokenKind.String && _open.Count == 0;
        }

        public void EndLine()
        {
            if (_tokensOnLine > 0 && !_commentOnly)
            {
                EndRun();
            }

            _tokensOnLine = 0;
            _commentOnly = true;
        }

        public void Finish()
        {
            EndRun();
            EndImports();
        }

        // A statement's first token on a line: an import starts or continues the run of imports, anything else ends it.
        private void Statement(ScriptTokenKind kind, ReadOnlySpan<char> text)
        {
            bool isImport = kind == ScriptTokenKind.Keyword && text.SequenceEqual("import");
            if (_inImport && _open.Count == 0 && _lastWasModule && !isImport)
            {
                _inImport = false;   // an import without a ';' ended at its module's string
            }

            if (_inImport)
            {
                return;
            }

            if (isImport && _open.Count == 0)
            {
                if (_importStart < 0)
                {
                    _importStart = Line;
                }

                _inImport = true;
                _importEnd = Line;
                return;
            }

            if (_open.Count == 0 || _importStart >= 0)
            {
                EndImports();
            }
        }

        private void EndImports()
        {
            if (_importStart >= 0 && _importEnd > _importStart)
            {
                Ranges.Add(new FoldingRange(_importStart, _importEnd, FoldingRangeKind.Imports));
            }

            _importStart = _importEnd = -1;
            _inImport = false;
        }

        private void Close()
        {
            if (_open.Count == 0)
            {
                return;
            }

            var (_, line) = _open[^1];
            _open.RemoveAt(_open.Count - 1);
            if (Line > line)
            {
                Ranges.Add(new FoldingRange(line, Line));
            }
        }

        private void LineComment(ReadOnlySpan<char> text, bool first)
        {
            var body = text[2..].TrimStart();
            if (body.StartsWith("#region"))
            {
                _regions.Add(Line);
                EndRun();
                return;
            }

            if (body.StartsWith("#endregion"))
            {
                EndRun();
                if (_regions.Count > 0)
                {
                    int start = _regions[^1];
                    _regions.RemoveAt(_regions.Count - 1);
                    if (Line > start)
                    {
                        Ranges.Add(new FoldingRange(start, Line, FoldingRangeKind.Marked));
                    }
                }

                return;
            }

            if (!first)
            {
                return;
            }

            if (_runStart >= 0 && _runEnd == Line - 1)
            {
                _runEnd = Line;
            }
            else
            {
                EndRun();
                _runStart = _runEnd = Line;
            }
        }

        private void EndRun()
        {
            if (_runStart >= 0 && _runEnd > _runStart)
            {
                Ranges.Add(new FoldingRange(_runStart, _runEnd, FoldingRangeKind.Comment));
            }

            _runStart = _runEnd = -1;
        }

        private void BlockComment(ReadOnlySpan<char> text)
        {
            if (text.StartsWith("/*"))
            {
                var body = text[2..].TrimStart();
                if (body.StartsWith("#region"))
                {
                    _regions.Add(Line);
                    return;
                }

                if (body.StartsWith("#endregion"))
                {
                    if (_regions.Count > 0)
                    {
                        int start = _regions[^1];
                        _regions.RemoveAt(_regions.Count - 1);
                        if (Line > start)
                        {
                            Ranges.Add(new FoldingRange(start, Line, FoldingRangeKind.Marked));
                        }
                    }

                    return;
                }

                _commentStart = Line;
            }

            if (text.Length >= 2 && text[^2..].SequenceEqual("*/") && !(text.StartsWith("/*") && text.Length < 4))
            {
                if (_commentStart >= 0 && Line > _commentStart)
                {
                    Ranges.Add(new FoldingRange(_commentStart, Line, FoldingRangeKind.Comment));
                }

                _commentStart = -1;
            }
        }
    }
}
