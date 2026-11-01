// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.CSharp.Project;

/// <summary>
/// XML's Format document (.csproj, .props, .targets, .xml, .xaml, .config, .resx, .slnx): each element, comment and
/// processing instruction on its own line, indented by the editor's step; an element holding only text kept on one line
/// (<c>&lt;TargetFramework&gt;net10.0&lt;/TargetFramework&gt;</c>), and an element with text among its children
/// (mixed content) or <c>xml:space="preserve"</c> kept exactly as it is inside; an empty element's start and end tags
/// joined; a start tag's attributes on further lines indented one step past the tag; a comment set apart by a space
/// on the line of the markup before it kept there; one empty line between siblings kept where there was one. Only the white space between
/// markup (and between a tag's attributes) changes. A text that is not well-formed (an element not closed, an end tag
/// that closes another) is left as it is, the message naming the line and column.
/// </summary>
internal sealed class XmlFormatter : ITextFormatter
{
    private enum Kind : byte
    {
        Start,
        Empty,
        End,
        Comment,
        CData,
        Instruction,
        Doctype,
        Text,
    }

    private readonly record struct Item(int Start, int End, Kind Kind, int NameStart, int NameEnd, bool Preserve);

    /// <inheritdoc />
    public void Format(ReadOnlySpan<char> text, FormatStyle style, List<TextEdit> edits)
    {
        var attributeGaps = new List<(int Item, int Start, int End)>();
        var items = Lex(text, attributeGaps);
        if (items.Count == 0)
        {
            return;
        }

        var kept = Structure(text, items);
        int depth = 0;
        int nextGap = 0;
        for (int k = 0; k <= items.Count; k++)
        {
            int gapStart = k == 0 ? 0 : items[k - 1].End;
            int gapEnd = k == items.Count ? text.Length : items[k].Start;
            var gap = text[gapStart..gapEnd];
            string? wanted = k == 0 ? ""
                : k == items.Count ? (gap.ContainsAny('\n', '\r') ? style.NewLine : "")
                : kept[k] ? null
                : Gap(items[k - 1], items[k], gap, depth, style, text);
            if (wanted is not null && !gap.SequenceEqual(wanted))
            {
                edits.Add(new TextEdit(gapStart, gapEnd - gapStart, wanted));
            }

            if (k == items.Count)
            {
                break;
            }

            // The tag's own lines: each attribute that starts a line, one step past the tag.
            while (nextGap < attributeGaps.Count && attributeGaps[nextGap].Item == k)
            {
                var (_, start, end) = attributeGaps[nextGap++];
                bool inside = kept[k];                       // a tag inside mixed or preserved content stays as it is
                string step = style.Break(depth + 1);
                if (!inside && !text[start..end].SequenceEqual(step))
                {
                    edits.Add(new TextEdit(start, end - start, step));
                }
            }

            depth += items[k].Kind == Kind.Start ? 1 : items[k].Kind == Kind.End ? -1 : 0;
        }
    }

    // The white space wanted between prev and cur, at `depth` (the nesting after prev); null keeps the source's.
    private static string? Gap(Item prev, Item cur, ReadOnlySpan<char> gap, int depth, FormatStyle style, ReadOnlySpan<char> text)
    {
        bool newline = gap.ContainsAny('\n', '\r');
        if (cur.Kind == Kind.End)
        {
            if (prev.Kind == Kind.Start && text[prev.NameStart..prev.NameEnd].SequenceEqual(text[cur.NameStart..cur.NameEnd]))
            {
                return "";                                   // <a>  </a> is <a></a>
            }

            return style.Break(depth - 1);
        }

        if (cur.Kind == Kind.Comment && !newline && !gap.IsEmpty)
        {
            return null;                                     // a comment after the markup on its line ("<A /> <!-- a -->") stays there
        }

        return style.Break(depth, BlankLines(gap));
    }

    private static int BlankLines(ReadOnlySpan<char> gap)
    {
        int lines = 0;
        for (int i = 0; i < gap.Length; i++)
        {
            if (gap[i] == '\n' || (gap[i] == '\r' && (i + 1 >= gap.Length || gap[i + 1] != '\n')))
            {
                lines++;
            }
        }

        return lines >= 2 ? 1 : 0;
    }

    // Whether each item's gap before it is kept as it is: inside an element whose content is mixed or preserved. Checks
    // that every element closes, with its own name.
    private static bool[] Structure(ReadOnlySpan<char> text, List<Item> items)
    {
        var kept = new bool[items.Count + 1];
        var open = new Stack<(int Index, bool Mixed)>();
        for (int k = 0; k < items.Count; k++)
        {
            var item = items[k];
            switch (item.Kind)
            {
                case Kind.Start:
                    open.Push((k, item.Preserve));
                    break;
                case Kind.Text or Kind.CData:
                    if (open.Count > 0 && !open.Peek().Mixed)
                    {
                        open.Push((open.Pop().Index, true));
                    }
                    else if (open.Count == 0 && item.Kind == Kind.Text)
                    {
                        throw ITextFormatter.Malformed(text, item.Start, "text outside the root element");
                    }

                    break;
                case Kind.End:
                {
                    if (open.Count == 0)
                    {
                        throw ITextFormatter.Malformed(text, item.Start, $"</{text[item.NameStart..item.NameEnd].ToString()}> closes no element");
                    }

                    var (index, mixed) = open.Pop();
                    var start = items[index];
                    if (!text[start.NameStart..start.NameEnd].SequenceEqual(text[item.NameStart..item.NameEnd]))
                    {
                        throw ITextFormatter.Malformed(text, item.Start,
                            $"</{text[item.NameStart..item.NameEnd].ToString()}> closes <{text[start.NameStart..start.NameEnd].ToString()}>");
                    }

                    if (mixed)
                    {
                        for (int j = index + 1; j <= k; j++)
                        {
                            kept[j] = true;
                        }
                    }

                    break;
                }
            }
        }

        if (open.Count > 0)
        {
            var start = items[open.Peek().Index];
            throw ITextFormatter.Malformed(text, start.Start, $"<{text[start.NameStart..start.NameEnd].ToString()}> is not closed");
        }

        return kept;
    }

    private static List<Item> Lex(ReadOnlySpan<char> text, List<(int Item, int Start, int End)> attributeGaps)
    {
        var items = new List<Item>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c is ' ' or '\t' or '\r' or '\n' or '﻿')
            {
                i++;
                continue;
            }

            if (c != '<')
            {
                int end = text[i..].IndexOf('<');
                end = end < 0 ? text.Length : i + end;
                int last = end;
                while (last > i && text[last - 1] is ' ' or '\t' or '\r' or '\n')
                {
                    last--;
                }

                items.Add(new Item(i, last, Kind.Text, 0, 0, false));
                i = end;
                continue;
            }

            var rest = text[i..];
            if (rest.StartsWith("<!--"))
            {
                items.Add(new Item(i, i = Through(text, i, "-->", "a comment is not closed (-->)"), Kind.Comment, 0, 0, false));
            }
            else if (rest.StartsWith("<![CDATA["))
            {
                items.Add(new Item(i, i = Through(text, i, "]]>", "a CDATA section is not closed (]]>)"), Kind.CData, 0, 0, false));
            }
            else if (rest.StartsWith("<?"))
            {
                items.Add(new Item(i, i = Through(text, i, "?>", "a processing instruction is not closed (?>)"), Kind.Instruction, 0, 0, false));
            }
            else if (rest.StartsWith("<!"))
            {
                int bracket = rest.IndexOf('[');
                int close = rest.IndexOf('>');
                string end = bracket >= 0 && bracket < close ? "]>" : ">";
                items.Add(new Item(i, i = Through(text, i, end, "a declaration is not closed (>)"), Kind.Doctype, 0, 0, false));
            }
            else
            {
                i = Tag(text, i, items, attributeGaps);
            }
        }

        return items;
    }

    private static int Through(ReadOnlySpan<char> text, int i, string close, string what)
    {
        int found = text[i..].IndexOf(close, StringComparison.Ordinal);
        if (found < 0)
        {
            throw ITextFormatter.Malformed(text, i, what);
        }

        return i + found + close.Length;
    }

    // A start, empty or end tag at i: its name, its attributes (the gaps before those that start a line recorded), its end.
    private static int Tag(ReadOnlySpan<char> text, int i, List<Item> items, List<(int Item, int Start, int End)> attributeGaps)
    {
        int start = i;
        bool end = i + 1 < text.Length && text[i + 1] == '/';
        int nameStart = end ? i + 2 : i + 1;
        int nameEnd = NameEnd(text, nameStart);
        if (nameEnd == nameStart)
        {
            throw ITextFormatter.Malformed(text, i, "a '<' that starts no tag (write &lt; in text)");
        }

        i = nameEnd;
        bool preserve = false;
        var gaps = new List<(int Start, int End)>();
        while (true)
        {
            int gap = i;
            while (i < text.Length && text[i] is ' ' or '\t' or '\r' or '\n')
            {
                i++;
            }

            if (i >= text.Length)
            {
                throw ITextFormatter.Malformed(text, start, $"<{text[nameStart..nameEnd].ToString()} is not closed (>)");
            }

            char c = text[i];
            if (c == '>' || (c == '/' && i + 1 < text.Length && text[i + 1] == '>'))
            {
                if (end && c == '/')
                {
                    throw ITextFormatter.Malformed(text, i, "an end tag that ends with '/>'");
                }

                i += c == '>' ? 1 : 2;
                int index = items.Count;
                items.Add(new Item(start, i, end ? Kind.End : c == '/' ? Kind.Empty : Kind.Start, nameStart, nameEnd, preserve));
                foreach (var (from, to) in gaps)
                {
                    attributeGaps.Add((index, from, to));
                }

                return i;
            }

            if (end || i == gap)
            {
                throw ITextFormatter.Malformed(text, i, $"unexpected '{c}' in <{(end ? "/" : "")}{text[nameStart..nameEnd].ToString()}>");
            }

            int attribute = i;
            int attributeEnd = NameEnd(text, i);
            if (attributeEnd == i)
            {
                throw ITextFormatter.Malformed(text, i, $"unexpected '{c}' in <{text[nameStart..nameEnd].ToString()}>");
            }

            i = attributeEnd;
            while (i < text.Length && text[i] is ' ' or '\t' or '\r' or '\n')
            {
                i++;
            }

            if (i >= text.Length || text[i] != '=')
            {
                throw ITextFormatter.Malformed(text, attribute, $"the attribute {text[attribute..attributeEnd].ToString()} has no value (=\"...\")");
            }

            i++;
            while (i < text.Length && text[i] is ' ' or '\t' or '\r' or '\n')
            {
                i++;
            }

            if (i >= text.Length || text[i] is not ('"' or '\''))
            {
                throw ITextFormatter.Malformed(text, attribute, $"the value of {text[attribute..attributeEnd].ToString()} is not in quotes");
            }

            char quote = text[i];
            int close = text[(i + 1)..].IndexOf(quote);
            if (close < 0)
            {
                throw ITextFormatter.Malformed(text, i, $"the value of {text[attribute..attributeEnd].ToString()} is not closed ({quote})");
            }

            var value = text[(i + 1)..(i + 1 + close)];
            preserve |= text[attribute..attributeEnd].SequenceEqual("xml:space") && value.SequenceEqual("preserve");
            if (text[gap..attribute].ContainsAny('\n', '\r'))
            {
                gaps.Add((gap, attribute));
            }

            i += close + 2;
        }
    }

    private static int NameEnd(ReadOnlySpan<char> text, int i)
    {
        if (i >= text.Length || !(char.IsLetter(text[i]) || text[i] is '_' or ':'))
        {
            return i;
        }

        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '-' or '.' or ':'))
        {
            i++;
        }

        return i;
    }
}
