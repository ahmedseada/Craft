// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using Craft.Abstractions;
using Craft.Web.Script;
using Craft.Web.Style;

namespace Craft.Web.Markup;

/// <summary>
/// An Angular project's TypeScript with its inline templates and styles ("angular-typescript", W-markup): TypeScript's
/// lexer, except that the text of a <c>template:</c> property's string or template literal is an Angular template and that
/// of <c>styles:</c> (one literal, or each literal of its array) is the project's inline style language (CSS, SCSS or LESS
/// by angular.json). The TypeScript state and the embedded one ride together, interned in <see cref="States"/>; a property
/// named so inside a string or a comment is left to TypeScript.
/// </summary>
public sealed class AngularTypeScriptTokenizer : LineTokenizer
{
    private static readonly ConcurrentDictionary<(int, StyleDialect), AngularTypeScriptTokenizer> Shared = new();
    private const ulong NoMode = 0;
    private const ulong TemplateMode = 1;
    private const ulong StylesMode = 2;
    private const ulong GapMode = 3;

    /// <summary>A lexer of Angular <paramref name="angularVersion"/>'s templates and <paramref name="styles"/> inline styles.</summary>
    public AngularTypeScriptTokenizer(int angularVersion, StyleDialect styles, LineStates? states = null)
    {
        AngularVersion = angularVersion;
        StyleDialect = styles;
        States = states ?? new LineStates();
        TypeScript = ScriptTokenizer.For(ScriptDialect.TypeScript, ScriptVersion.Newest);
        Html = HtmlTokenizer.For(HtmlOptions.AngularTemplate with { AngularVersion = angularVersion });
        Css = StyleTokenizer.For(styles);
    }

    /// <summary>The Angular version its templates read.</summary>
    public int AngularVersion { get; }

    /// <summary>The style language of its inline styles.</summary>
    public StyleDialect StyleDialect { get; }

    /// <summary>The table its states are interned in.</summary>
    public LineStates States { get; }

    /// <summary>The TypeScript lexer it reads code with.</summary>
    public ScriptTokenizer TypeScript { get; }

    /// <summary>The template lexer.</summary>
    public HtmlTokenizer Html { get; }

    /// <summary>The inline styles' lexer.</summary>
    public StyleTokenizer Css { get; }

    /// <summary>The shared lexer of <paramref name="angularVersion"/> and <paramref name="styles"/> (made once).</summary>
    public static AngularTypeScriptTokenizer For(int angularVersion, StyleDialect styles = StyleDialect.Css) =>
        Shared.GetOrAdd((angularVersion, styles), static key => new AngularTypeScriptTokenizer(key.Item1, key.Item2));

    /// <inheritdoc />
    public override ulong Tokenize(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into)
    {
        Decode(state, out ulong ts, out ulong mode, out ulong embedded);
        int i = 0;
        while (true)
        {
            ulong kind = mode & 0x3;
            if (kind is TemplateMode or StylesMode)
            {
                char quote = QuoteOf(mode);
                int close = Close(line, i, quote);
                int stop = close < 0 ? line.Length : close;
                embedded = kind == TemplateMode
                    ? Html.Tokenize(line[i..stop], lineStart + i, embedded, into)
                    : Css.Tokenize(line[i..stop], lineStart + i, embedded, into);
                if (close < 0)
                {
                    return Encode(ts, mode, embedded);
                }

                into?.Add(lineStart + close, 1, TokenClasses.String);
                ts = TypeScript.WithPrevValue(ts);
                i = close + 1;
                mode = (mode & 0x10) != 0 ? GapMode | 0x10 : NoMode;
                embedded = 0;
                continue;
            }

            if (kind == GapMode)
            {
                int j = i;
                while (j < line.Length && line[j] is ' ' or '\t' or ',')
                {
                    j++;
                }

                if (j > i)
                {
                    ts = TypeScript.Tokenize(line[i..j], lineStart + i, ts, into);
                }

                if (j >= line.Length)
                {
                    return Encode(ts, mode, 0);
                }

                if (line[j] is '`' or '\'' or '"')
                {
                    into?.Add(lineStart + j, 1, TokenClasses.String);
                    mode = StylesMode | 0x10 | QuoteBits(line[j]);
                    embedded = 0;
                    i = j + 1;
                    continue;
                }

                mode = NoMode;
                i = j;
                continue;
            }

            if (i > line.Length)
            {
                return Encode(ts, NoMode, 0);
            }

            int found = FindKey(line, i, out int value, out bool template, out bool array);
            if (found < 0)
            {
                ts = TypeScript.Tokenize(line[i..], lineStart + i, ts, into);
                return Encode(ts, NoMode, 0);
            }

            ts = TypeScript.Tokenize(line[i..value], lineStart + i, ts, into);
            if (TypeScript.TopOf(ts, out var sub) is FrameKind.Code or FrameKind.TemplateExpr && sub == Sub.None)
            {
                into?.Add(lineStart + value, 1, TokenClasses.String);
                mode = (template ? TemplateMode : StylesMode) | QuoteBits(line[value]) | (array ? 0x10UL : 0);
                embedded = 0;
                i = value + 1;
                continue;
            }

            i = value;   // the key was inside a string or a comment: TypeScript reads on
            if (i >= line.Length)
            {
                return Encode(ts, NoMode, 0);
            }

            ts = TypeScript.Tokenize(line[i..(i + 1)], lineStart + i, ts, into);
            i++;
        }
    }

    /// <inheritdoc />
    public override LanguageRules? RulesAt(ReadOnlySpan<char> line, int column, ulong state)
    {
        Decode(state, out _, out ulong mode, out ulong embedded);
        return (mode & 0x3) switch
        {
            TemplateMode => Html.RulesAt(line, column, embedded) ?? HtmlTokenizer.HtmlRules,
            StylesMode => HtmlTokenizer.StyleRules,
            _ => null,
        };
    }

    // Whether a line in `state` starts inside an inline template or style (for the folding and the formatter).
    internal bool IsEmbedded(ulong state)
    {
        Decode(state, out _, out ulong mode, out _);
        return (mode & 0x3) is TemplateMode or StylesMode;
    }

    private void Decode(ulong state, out ulong ts, out ulong mode, out ulong embedded)
    {
        mode = NoMode;
        embedded = 0;
        if (!LineStates.IsInterned(state))
        {
            ts = state;
            return;
        }

        var frames = States.Frames(state);
        ts = frames[0];
        if (frames.Length > 1)
        {
            mode = frames[1];
            embedded = frames.Length > 2 ? frames[2] : 0;
        }
    }

    // A plain TypeScript state stays plain; one TypeScript interned, or one with an inline part, is interned here.
    private ulong Encode(ulong ts, ulong mode, ulong embedded)
    {
        if (mode == NoMode)
        {
            if (!LineStates.IsInterned(ts))
            {
                return ts;
            }

            Span<ulong> one = [ts];
            return States.Intern(one);
        }

        Span<ulong> frames = [ts, mode, embedded];
        return States.Intern(frames);
    }

    private static ulong QuoteBits(char quote) => quote switch { '\'' => 0x4UL, '"' => 0x8UL, _ => 0UL };

    private static char QuoteOf(ulong mode) => (mode & 0xC) switch { 0x4 => '\'', 0x8 => '"', _ => '`' };

    // The literal's closing quote from i (a '\' escapes), or -1.
    private static int Close(ReadOnlySpan<char> line, int i, char quote)
    {
        for (int j = i; j < line.Length; j++)
        {
            if (line[j] == '\\')
            {
                j++;
            }
            else if (line[j] == quote)
            {
                return j;
            }
        }

        return -1;
    }

    // The next `template:` or `styles:` (`styles: [`) followed by a quote or a backtick: the key's start, the quote's index.
    private static int FindKey(ReadOnlySpan<char> line, int from, out int value, out bool template, out bool array)
    {
        value = -1;
        template = false;
        array = false;
        int i = from;
        while (i < line.Length)
        {
            int t = line[i..].IndexOf("template", StringComparison.Ordinal);
            int s = line[i..].IndexOf("styles", StringComparison.Ordinal);
            if (t < 0 && s < 0)
            {
                return -1;
            }

            bool isTemplate = t >= 0 && (s < 0 || t < s);
            int start = i + (isTemplate ? t : s);
            int end = start + (isTemplate ? 8 : 6);
            i = end;
            if ((start > 0 && (char.IsAsciiLetterOrDigit(line[start - 1]) || line[start - 1] is '_' or '$' or '.')) || (end < line.Length && (char.IsAsciiLetterOrDigit(line[end]) || line[end] is '_' or '$')))
            {
                continue;
            }

            int k = Skip(line, end);
            if (k >= line.Length || line[k] != ':')
            {
                continue;
            }

            k = Skip(line, k + 1);
            bool inArray = false;
            if (!isTemplate && k < line.Length && line[k] == '[')
            {
                inArray = true;
                k = Skip(line, k + 1);
            }

            if (k < line.Length && line[k] is '`' or '\'' or '"')
            {
                value = k;
                template = isTemplate;
                array = inArray;
                return start;
            }
        }

        return -1;

        static int Skip(ReadOnlySpan<char> line, int i)
        {
            while (i < line.Length && line[i] is ' ' or '\t')
            {
                i++;
            }

            return i;
        }
    }
}
