// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;
using Craft.Web.Style;

namespace Craft.Web.Markup;

/// <summary>
/// The built-in completion of HTML and Angular templates (no Node, no language server): element names after <c>&lt;</c>;
/// inside a start tag the global attributes, the element's own, the ARIA attributes, <c>data-</c> and the event handlers;
/// a fixed list's values (<c>type</c> of input and button, <c>rel</c>, <c>target</c>, <c>dir</c>, booleans); after <c>&lt;/</c>
/// the open elements, the innermost first; after a start tag's <c>&gt;</c> its end tag; entities after <c>&amp;</c>; the CSS of
/// a <c>&lt;style&gt;</c> and of <c>style="…"</c> (<see cref="StyleCompletion"/>). In Angular templates also the control flow
/// blocks after <c>@</c> as snippets and the element's properties and events after <c>[</c>, <c>(</c> and <c>[(</c>. The
/// context is the HTML lexer's at the caret, from the state the caret's line starts in; the document is read from the top only
/// when the context needs what lies above that line (the open elements, a tag begun on an earlier line, a style's start).
/// </summary>
public sealed class HtmlCompletion : ILineCompletion
{
    private static readonly Snippet[] AngularBlocks =
    [
        new("@if", ["if"], "@if (${1:condition}) {\n\t$0\n}", "Angular @if block"),
        new("@else", ["else"], "@else {\n\t$0\n}", "Angular @else block"),
        new("@for", ["for"], "@for (${1:item} of ${2:items}; track ${3:item.id}) {\n\t$0\n}", "Angular @for block"),
        new("@empty", ["empty"], "@empty {\n\t$0\n}", "Angular @empty block"),
        new("@switch", ["switch"], "@switch (${1:value}) {\n\t@case (${2:match}) {\n\t\t$0\n\t}\n}", "Angular @switch block"),
        new("@case", ["case"], "@case (${1:match}) {\n\t$0\n}", "Angular @case block"),
        new("@default", ["default"], "@default {\n\t$0\n}", "Angular @default block"),
        new("@defer", ["defer"], "@defer (on ${1|viewport,idle,interaction,hover,immediate,timer(500ms)|}) {\n\t$0\n} @placeholder {\n\t\n}", "Angular @defer block"),
        new("@placeholder", ["placeholder"], "@placeholder {\n\t$0\n}", "Angular @placeholder block"),
        new("@loading", ["loading"], "@loading {\n\t$0\n}", "Angular @loading block"),
        new("@error", ["error"], "@error {\n\t$0\n}", "Angular @error block"),
        new("@let", ["let"], "@let ${1:name} = ${2:value};", "Angular @let declaration"),
    ];

    /// <summary>The one instance (it holds nothing per document).</summary>
    public static HtmlCompletion Instance { get; } = new();

    /// <summary>The Angular blocks it offers after <c>@</c>, as snippets.</summary>
    public static IReadOnlyList<Snippet> Blocks => AngularBlocks;

    /// <inheritdoc />
    public bool IsTriggerCharacter(char c) => c is '<' or '/' or '&' or '@' or '[' or '(' or '*' or '"' or '\'' or '=' or ' ' or '>' or ':';

    /// <inheritdoc />
    public bool IsWordCharacter(char c) => c is '-' or ':' or '.';

    /// <inheritdoc />
    public CompletionList? Complete(LineCompletionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Tokenizer is not HtmlTokenizer html)
        {
            return null;
        }

        var snapshot = request.Snapshot;
        int position = request.Position;
        int index = snapshot.GetLineIndex(position);
        var line = snapshot.GetLine(index);
        int length = position - line.Start;
        char[] buffer = ArrayPool<char>.Shared.Rent(Math.Max(1, length));
        try
        {
            var prefix = buffer.AsSpan(0, length);
            snapshot.CopyTo(line.Start, prefix);
            var near = new Context(snapshot, buffer, line.Start);
            ulong state = html.Lex(prefix, line.Start, request.LineState, null, near);
            cancellationToken.ThrowIfCancellationRequested();
            var mode = html.ModeOf(state, out _, out _);
            var attribute = html.AttributeOf(state, out bool afterEquals);
            int wordStart = length;
            while (wordStart > 0 && IsName(prefix[wordStart - 1]))
            {
                wordStart--;
            }

            char before = wordStart > 0 ? prefix[wordStart - 1] : '\0';
            char before2 = wordStart > 1 ? prefix[wordStart - 2] : '\0';
            int at = line.Start + wordStart;
            int wordEnd = WordEnd(snapshot, position);
            var items = new List<CompletionItem>();
            Context? far = null;
            int upTo = line.Start + wordStart;   // the word being typed is left out (a typed </div must still offer div)
            Context Far() => far ??= Scan(html, snapshot, index, upTo, cancellationToken);

            // After a start tag's '>': its end tag.
            if (near.LastKind == MarkupTokenKind.TagEnd && near.LastEnd == position && !near.LastClosing)
            {
                string? name = near.Ended ?? Far().Ended;
                if (name is not null && !HtmlNames.Void.Contains(name) && !Follows(snapshot, position, "</"))
                {
                    items.Add(new CompletionItem("</" + name + ">", CompletionItemKind.Keyword, "end tag", new TextEdit(position, 0, "</" + name + ">"), "", "0", position));
                    return new CompletionList(snapshot, position, position, items);
                }

                return null;
            }

            switch (mode)
            {
                case MarkupMode.Text when before == '<' && wordStart == length:
                    TagNames(html, at, wordEnd, items);
                    break;
                case MarkupMode.Text when before == '/' && before2 == '<':
                    EndTags(Far().Open, snapshot, at, wordEnd, items);
                    break;
                case MarkupMode.Text when before == '&':
                    Entities(snapshot, at, wordEnd, items);
                    break;
                case MarkupMode.Text when before == '@' && html.Options.Angular && html.Options.AngularVersion >= 1700:
                    foreach (var block in AngularBlocks)
                    {
                        if (block.Name == "@let" && html.Options.AngularVersion < 1801)
                        {
                            continue;
                        }

                        items.Add(new CompletionItem(block.Name, CompletionItemKind.Snippet, block.Description, new TextEdit(at - 1, wordEnd - at + 1, block.Name), block.Prefixes[0], block.Prefixes[0], Data: block));
                    }

                    break;
                case MarkupMode.Tag when near.LastKind == MarkupTokenKind.TagName && near.LastEnd == position && !near.LastClosing:
                    TagNames(html, at, wordEnd, items);
                    break;
                case MarkupMode.Tag when afterEquals:
                    Values(near.Tag ?? Far().Tag, near.Attribute ?? Far().Attribute, snapshot, at, wordEnd, quoted: false, items);
                    break;
                case MarkupMode.Tag:
                    Attributes(html.Options.Angular, near.Tag ?? Far().Tag, before, before2, snapshot, at, wordEnd, items);
                    break;
                case MarkupMode.CloseTag when near.LastKind == MarkupTokenKind.TagName && near.LastEnd == position:
                    EndTags(Far().Open, snapshot, at, wordEnd, items);
                    break;
                case MarkupMode.AttrDouble or MarkupMode.AttrSingle when attribute == AttributeKind.Style:
                    {
                        int start = near.ValueStart >= 0 ? near.ValueStart : Far().ValueStart;
                        char quote = mode == MarkupMode.AttrDouble ? '"' : '\'';
                        int end = FindForward(snapshot, position, quote.ToString(), 1 << 16);
                        return start < 0 ? null : StyleCompletion.Complete(snapshot, start, end < 0 ? snapshot.Length : end, position, StyleDialect.Css, StyleNames.Latest, declarations: true, cancellationToken);
                    }

                case MarkupMode.AttrDouble or MarkupMode.AttrSingle when attribute is AttributeKind.Plain or AttributeKind.Type or AttributeKind.Interpolated:
                    Values(near.Tag ?? Far().Tag, near.Attribute ?? Far().Attribute, snapshot, at, wordEnd, quoted: true, items);
                    break;
                case MarkupMode.Style:
                    {
                        int start = near.StyleStart >= 0 ? near.StyleStart : Far().StyleStart;
                        int end = FindForward(snapshot, position, "</style", int.MaxValue);
                        return start < 0 ? null : StyleCompletion.Complete(snapshot, start, end < 0 ? snapshot.Length : end, position, StyleDialect.Css, StyleNames.Latest, declarations: false, cancellationToken);
                    }
            }

            return items.Count == 0 ? null : new CompletionList(snapshot, position, at, items);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static bool IsName(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.';

    private static int WordEnd(ITextSnapshot snapshot, int position)
    {
        int end = position;
        var line = snapshot.GetLine(snapshot.GetLineIndex(position));
        while (end < line.End && IsName(snapshot[end]))
        {
            end++;
        }

        return end;
    }

    // Whether the text at `position` starts with `text`.
    private static bool Follows(ITextSnapshot snapshot, int position, string text)
    {
        if (position + text.Length > snapshot.Length)
        {
            return false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            if (snapshot[position + i] != text[i])
            {
                return false;
            }
        }

        return true;
    }

    // The end of an edit that also takes the closers the editor put after the caret (an auto-closed ']', ')' or '>').
    private static int Over(ITextSnapshot snapshot, int end, string closers) => Follows(snapshot, end, closers) ? end + closers.Length : end;

    // The first `needle` (any case) at or after `from`, within `limit` characters, or -1.
    private static int FindForward(ITextSnapshot snapshot, int from, string needle, int limit)
    {
        Span<char> chunk = stackalloc char[512];
        int stop = (int)Math.Min((long)snapshot.Length, (long)from + limit);
        int at = from;
        while (at < stop)
        {
            int count = Math.Min(chunk.Length, stop - at);
            snapshot.CopyTo(at, chunk[..count]);
            int found = chunk[..count].IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (found >= 0)
            {
                return at + found;
            }

            if (count < chunk.Length)
            {
                break;
            }

            at += count - needle.Length + 1;
        }

        return -1;
    }

    private static void TagNames(HtmlTokenizer html, int at, int end, List<CompletionItem> items)
    {
        foreach (var name in HtmlData.AllElements)
        {
            items.Add(new CompletionItem(name, CompletionItemKind.Keyword, "element", new TextEdit(at, end - at, name), name, name));
        }

        if (html.Options.Angular)
        {
            foreach (var name in (string[])["ng-container", "ng-template", "ng-content", "router-outlet"])
            {
                items.Add(new CompletionItem(name, CompletionItemKind.Class, "Angular", new TextEdit(at, end - at, name), name, name));
            }
        }
    }

    private static void EndTags(List<string> open, ITextSnapshot snapshot, int at, int end, List<CompletionItem> items)
    {
        int stop = Over(snapshot, end, ">");
        for (int i = open.Count - 1; i >= 0; i--)
        {
            string name = open[i];
            items.Add(new CompletionItem("</" + name + ">", CompletionItemKind.Keyword, i == open.Count - 1 ? "closes the innermost element" : "end tag",
                new TextEdit(at, stop - at, name + ">"), name, (open.Count - i).ToString("D3", System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    private static void Entities(ITextSnapshot snapshot, int at, int end, List<CompletionItem> items)
    {
        int stop = Over(snapshot, end, ";");
        foreach (var name in HtmlData.Entities)
        {
            items.Add(new CompletionItem("&" + name + ";", CompletionItemKind.Constant, "character reference", new TextEdit(at, stop - at, name + ";"), name, name));
        }
    }

    private static void Attributes(bool angular, string? element, char before, char before2, ITextSnapshot snapshot, int at, int end, List<CompletionItem> items)
    {
        string tag = element ?? "";
        if (angular && before == '(' && before2 == '[')
        {
            int stop = Over(snapshot, end, ")]");
            foreach (var property in HtmlData.AngularPropertiesOf(tag))
            {
                items.Add(Binding(property, ")]", stop, at, CompletionItemKind.Property, "two-way binding"));
            }

            return;
        }

        if (angular && before == '[')
        {
            int stop = Over(snapshot, end, "]");
            foreach (var property in HtmlData.AngularPropertiesOf(tag))
            {
                items.Add(Binding(property, "]", stop, at, CompletionItemKind.Property, "property binding"));
            }

            return;
        }

        if (angular && before == '(')
        {
            int stop = Over(snapshot, end, ")");
            foreach (var name in HtmlData.AngularEventsOf(tag))
            {
                items.Add(Binding(name, ")", stop, at, CompletionItemKind.Event, "event binding"));
            }

            return;
        }

        if (angular && before == '*')
        {
            foreach (var directive in HtmlData.Directives)
            {
                if (directive[0] == '*')
                {
                    string name = directive[1..];
                    items.Add(new CompletionItem(directive, CompletionItemKind.Keyword, "structural directive", new TextEdit(at, end - at, name + "=\"\""), name, name, at + name.Length + 2));
                }
            }

            return;
        }

        bool hasValue = end < snapshot.Length && snapshot[end] == '=';
        Add(HtmlData.AttributesOf(tag), "0", CompletionItemKind.Property, "attribute of <" + tag + ">");
        Add(HtmlData.Globals, "1", CompletionItemKind.Property, "global attribute");
        Add(HtmlData.Aria, "2", CompletionItemKind.Property, "ARIA");
        Add(HtmlData.EventHandlers, "3", CompletionItemKind.Event, "event handler");
        if (angular)
        {
            Add(HtmlData.Directives, "4", CompletionItemKind.Keyword, "Angular directive");
        }

        void Add(IReadOnlyList<string> names, string group, CompletionItemKind kind, string detail)
        {
            foreach (var name in names)
            {
                bool bare = hasValue || name.EndsWith('-') || HtmlData.IsBoolean(name);
                string text = bare ? name : name + "=\"\"";
                items.Add(new CompletionItem(name, kind, detail, new TextEdit(at, end - at, text), name, group + name, bare ? -1 : at + name.Length + 2));
            }
        }
    }

    private static CompletionItem Binding(string name, string closer, int stop, int at, CompletionItemKind kind, string detail)
    {
        string text = name.EndsWith('.') ? name : name + closer + "=\"\"";
        return new CompletionItem(name, kind, detail, new TextEdit(at, stop - at, text), name, name, name.EndsWith('.') ? -1 : at + name.Length + closer.Length + 2);
    }

    private static void Values(string? element, string? attribute, ITextSnapshot snapshot, int at, int end, bool quoted, List<CompletionItem> items)
    {
        if (attribute is null)
        {
            return;
        }

        foreach (var value in HtmlData.ValuesOf(element ?? "", attribute))
        {
            string text = quoted ? value : "\"" + value + "\"";
            items.Add(new CompletionItem(value, CompletionItemKind.EnumMember, attribute, new TextEdit(at, end - at, text), value, value));
        }
    }

    // The document from the top to the caret, read with a context that keeps what the near one cannot see.
    private static Context Scan(HtmlTokenizer html, ITextSnapshot snapshot, int index, int upTo, CancellationToken cancellationToken)
    {
        char[] buffer = ArrayPool<char>.Shared.Rent(256);
        var context = new Context(snapshot, buffer, 0);
        try
        {
            ulong state = html.Initial;
            for (int i = 0; i < index; i++)
            {
                if ((i & 255) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var line = snapshot.GetLine(i);
                if (buffer.Length < line.Length)
                {
                    ArrayPool<char>.Shared.Return(buffer);
                    buffer = ArrayPool<char>.Shared.Rent(line.Length);
                }

                snapshot.CopyTo(line.Start, buffer.AsSpan(0, line.Length));
                context.Use(buffer, line.Start);
                state = html.Lex(buffer.AsSpan(0, line.Length), line.Start, state, null, context);
            }

            var caretLine = snapshot.GetLine(index);
            int length = upTo - caretLine.Start;
            if (buffer.Length < length)
            {
                ArrayPool<char>.Shared.Return(buffer);
                buffer = ArrayPool<char>.Shared.Rent(length);
            }

            snapshot.CopyTo(caretLine.Start, buffer.AsSpan(0, length));
            context.Use(buffer, caretLine.Start);
            html.Lex(buffer.AsSpan(0, length), caretLine.Start, state, null, context);
            return context;
        }
        finally
        {
            context.Use([], 0);
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    // What the lexer told so far: the open elements, the tag and attribute being written, where a style's text and a value
    // start. Names are kept as a known element's own string or a place in the snapshot, made into text only when an item
    // needs them (nothing is allocated per tag or attribute read).
    private sealed class Context(ITextSnapshot snapshot, char[] text, int lineStart) : IMarkupSink
    {
        private readonly ITextSnapshot _snapshot = snapshot;
        private readonly List<Element> _open = [];
        private char[] _text = text;
        private int _lineStart = lineStart;
        private char[] _scratch = [];
        private bool _afterEquals;
        private Element? _tag;
        private Element? _ended;
        private int _attributeStart = -1;
        private int _attributeLength;

        private readonly record struct Element(string? Known, int Start, int Length);

        public string? Tag => _tag is { } tag ? NameOf(tag) : null;

        public string? Attribute => _attributeStart < 0 ? null : _snapshot.GetText(_attributeStart, _attributeLength);

        public string? Ended => _ended is { } ended ? NameOf(ended) : null;

        public List<string> Open => [.. _open.Select(NameOf)];

        public int ValueStart { get; private set; } = -1;

        public int StyleStart { get; private set; } = -1;

        public MarkupTokenKind LastKind { get; private set; } = MarkupTokenKind.Text;

        public int LastEnd { get; private set; } = -1;

        public bool LastClosing { get; private set; }

        public void Use(char[] text, int lineStart)
        {
            _text = text;
            _lineStart = lineStart;
        }

        public void Token(int start, int length, MarkupTokenKind kind)
        {
            var span = _text.AsSpan(start - _lineStart, length);
            switch (kind)
            {
                case MarkupTokenKind.TagOpen:
                    LastClosing = false;
                    _tag = null;
                    _attributeStart = -1;
                    break;
                case MarkupTokenKind.CloseTagOpen:
                    LastClosing = true;
                    _tag = null;
                    break;
                case MarkupTokenKind.TagName:
                    {
                        var element = new Element(HtmlData.ElementNames.TryGetValue(span, out string? known) ? known : null, start, length);
                        _tag = element;
                        if (!LastClosing)
                        {
                            while (_open.Count > 0 && Implied(span, _open[^1]))
                            {
                                _open.RemoveAt(_open.Count - 1);
                            }
                        }
                        else
                        {
                            for (int i = _open.Count - 1; i >= 0; i--)
                            {
                                if (span.Equals(SpanOf(_open[i]), StringComparison.OrdinalIgnoreCase))
                                {
                                    _open.RemoveRange(i, _open.Count - i);
                                    break;
                                }
                            }
                        }

                        break;
                    }

                case MarkupTokenKind.AttrName:
                    _attributeStart = start;
                    _attributeLength = length;
                    _afterEquals = false;
                    break;
                case MarkupTokenKind.AttrEquals:
                    _afterEquals = true;
                    break;
                case MarkupTokenKind.AttrValue when _afterEquals:
                    ValueStart = span.Length > 0 && span[0] is '"' or '\'' ? start + 1 : start;
                    _afterEquals = false;
                    break;
                case MarkupTokenKind.TagEnd when !LastClosing && _tag is { } tag:
                    if (!HtmlNames.Void.Contains(SpanOf(tag)))
                    {
                        _open.Add(tag);
                    }

                    _ended = tag;
                    if (tag.Known == "style")
                    {
                        StyleStart = start + length;
                    }

                    break;
                case MarkupTokenKind.TagEnd or MarkupTokenKind.SelfClose:
                    _ended = null;
                    break;
            }

            LastKind = kind;
            LastEnd = start + length;
        }

        // Whether opening `name` closes the open element (an optional end tag implied).
        private bool Implied(ReadOnlySpan<char> name, Element open)
        {
            var openName = SpanOf(open);
            return HtmlNames.OptionalEnd.Contains(openName) && HtmlNames.Implies(name, openName);
        }

        private ReadOnlySpan<char> SpanOf(Element element)
        {
            if (element.Known is { } known)
            {
                return known;
            }

            if (_scratch.Length < element.Length)
            {
                _scratch = new char[Math.Max(64, element.Length)];
            }

            _snapshot.CopyTo(element.Start, _scratch.AsSpan(0, element.Length));
            return _scratch.AsSpan(0, element.Length);
        }

        private string NameOf(Element element) => element.Known ?? _snapshot.GetText(element.Start, element.Length).ToLowerInvariant();
    }
}
