// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.Web.Markup;

/// <summary>
/// The folding ranges of HTML and Angular templates (W-markup), read by the lexer in one pass: every element over several
/// lines (from its <c>&lt;</c> to its end tag's <c>&gt;</c>; an element whose end tag is left out ends on the line before
/// what closed it; void elements never), a start tag over several lines, Angular's blocks (from <c>@if</c> to its
/// <c>}</c>, or the line before a <c>} @else {</c>), comments over several lines and <c>&lt;!-- #region --&gt;</c> to
/// <c>&lt;!-- #endregion --&gt;</c> (marked).
/// </summary>
public sealed class HtmlFolding : IFoldingRangeProvider
{
    private readonly Func<string, HtmlTokenizer> _tokenizer;

    /// <summary>A provider named <paramref name="name"/> for <paramref name="languages"/>, lexing with what <paramref name="tokenizer"/> gives for a language name when the request's session is not a line-state one.</summary>
    public HtmlFolding(string name, IReadOnlyList<string> languages, Func<string, HtmlTokenizer> tokenizer)
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
    public string Summary => "HTML and Angular templates: elements, start tags, blocks, comments and #region markers over several lines";

    /// <inheritdoc />
    public IReadOnlyList<string> Languages { get; }

    /// <inheritdoc />
    public Task<IReadOnlyList<FoldingRange>> GetRangesAsync(FoldingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tokenizer = request.Session is LineStateSession { Tokenizer: HtmlTokenizer own } ? own : _tokenizer(request.Language);
        return Task.FromResult<IReadOnlyList<FoldingRange>>(Ranges(tokenizer, request.Snapshot, cancellationToken));
    }

    /// <summary>The ranges of <paramref name="snapshot"/> read with <paramref name="tokenizer"/>.</summary>
    public static List<FoldingRange> Ranges(HtmlTokenizer tokenizer, ITextSnapshot snapshot, CancellationToken cancellationToken = default)
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
                sink.LineLength = line.Length;
                state = tokenizer.Lex(text, line.Start, state, null, sink);
            }

            return sink.Ranges;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private sealed class Sink : IMarkupSink
    {
        private readonly List<(string Name, int Line, bool Optional, bool Block)> _open = [];
        private readonly List<int> _regions = [];
        private string _tag = "";
        private int _tagLine;
        private bool _closing;
        private int _comment = -1;
        private int _blockLine = -1;

        public List<FoldingRange> Ranges { get; } = [];

        public int Line { get; set; }

        public char[] Text { get; set; } = [];

        public int LineStart { get; set; }

        public int LineLength { get; set; }

        public void Token(int start, int length, MarkupTokenKind kind)
        {
            var text = Text.AsSpan(start - LineStart, length);
            switch (kind)
            {
                case MarkupTokenKind.TagOpen:
                    _tagLine = Line;
                    _closing = false;
                    break;
                case MarkupTokenKind.CloseTagOpen:
                    _tagLine = Line;
                    _closing = true;
                    break;
                case MarkupTokenKind.TagName when !_closing:
                    _tag = text.ToString().ToLowerInvariant();
                    while (_open.Count > 0 && _open[^1] is { Optional: true, Block: false } open && HtmlNames.Implies(_tag, open.Name))
                    {
                        Close(Line - 1);
                    }

                    break;
                case MarkupTokenKind.TagName:
                    {
                        string name = text.ToString().ToLowerInvariant();
                        int at = _open.FindLastIndex(o => !o.Block && o.Name == name);
                        while (at >= 0 && _open.Count > at + 1 && _open[^1].Optional && !_open[^1].Block)
                        {
                            Close(Line - 1);
                        }

                        _tag = at >= 0 && _open.Count == at + 1 ? name : "";
                        break;
                    }

                case MarkupTokenKind.TagEnd:
                    if (!HtmlNames.Void.Contains(_tag))
                    {
                        _open.Add((_tag, _tagLine, HtmlNames.OptionalEnd.Contains(_tag), false));
                    }
                    else if (Line > _tagLine)
                    {
                        Ranges.Add(new FoldingRange(_tagLine, Line));
                    }

                    break;
                case MarkupTokenKind.SelfClose:
                    if (Line > _tagLine)
                    {
                        Ranges.Add(new FoldingRange(_tagLine, Line));
                    }

                    break;
                case MarkupTokenKind.CloseTagEnd when _tag.Length > 0 && _open.Count > 0 && !_open[^1].Block:
                    Close(Line);
                    _tag = "";
                    break;
                case MarkupTokenKind.BlockKeyword:
                    _blockLine = Line;
                    break;
                case MarkupTokenKind.BlockOpen:
                    _open.Add(("", _blockLine < 0 ? Line : _blockLine, false, true));
                    _blockLine = -1;
                    break;
                case MarkupTokenKind.BlockClose:
                    while (_open.Count > 0 && !_open[^1].Block && _open[^1].Optional)
                    {
                        Close(Line - 1);
                    }

                    if (_open.Count > 0 && _open[^1].Block)
                    {
                        var block = _open[^1];
                        _open.RemoveAt(_open.Count - 1);
                        bool more = Text.AsSpan((start + length - LineStart)..LineLength).Trim().Length > 0;   // } @else {: the block ends a line above
                        int end = more && Line - 1 > block.Line ? Line - 1 : Line;
                        if (end > block.Line)
                        {
                            Ranges.Add(new FoldingRange(block.Line, end));
                        }
                    }

                    break;
                case MarkupTokenKind.Comment:
                    Comment(text);
                    break;
            }
        }

        private void Close(int end)
        {
            var open = _open[^1];
            _open.RemoveAt(_open.Count - 1);
            if (end > open.Line)
            {
                Ranges.Add(new FoldingRange(open.Line, end));
            }
        }

        private void Comment(ReadOnlySpan<char> text)
        {
            bool starts = text.StartsWith("<!--");
            bool ends = text.EndsWith("-->") && !(starts && text.Length < 7);
            if (starts)
            {
                var body = text[4..].TrimStart();
                if (body.StartsWith("#region"))
                {
                    _regions.Add(Line);
                    return;
                }

                if (body.StartsWith("#endregion"))
                {
                    if (_regions.Count > 0)
                    {
                        int region = _regions[^1];
                        _regions.RemoveAt(_regions.Count - 1);
                        if (Line > region)
                        {
                            Ranges.Add(new FoldingRange(region, Line, FoldingRangeKind.Marked));
                        }
                    }

                    return;
                }

                _comment = Line;
            }

            if (ends && _comment >= 0)
            {
                if (Line > _comment)
                {
                    Ranges.Add(new FoldingRange(_comment, Line, FoldingRangeKind.Comment));
                }

                _comment = -1;
            }
        }
    }
}
