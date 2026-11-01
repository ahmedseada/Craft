// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;
using Craft.CSharp;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// Phase W's kit (W0): the line-state kit public in Craft.Abstractions (<see cref="LineTokenizer"/>,
/// <see cref="LineStateSession"/>, <see cref="ITextFormatter"/>, <see cref="FormatStyle"/>, <see cref="LineStateLanguage"/>),
/// used here from a test-only language written as a third party would write it (states past 64 bits interned in a
/// <see cref="LineStates"/> table, an embedded part with its own rules); a session's dialect change; the interning table's
/// bound and its reset fallback; <see cref="ILanguageSession.RulesAt"/> and toggle comment's block-comment fallback.
/// </summary>
[TestGroup("WebKit")]
public static class WebKitTests
{
    // "Stanza", a test-only language: `<<` `>>` and `[[` `]]` nest to any depth across lines (frames, interned past 64
    // bits), words inside a frame are strings, `/* */` comments span lines at the top level, a `[[` part comments with `//`.
    private static readonly LanguageRules StanzaRules = new(null, "/*", "*/", [new('<', '>'), new('[', ']')]);
    private static readonly LanguageRules ScriptRules = new("//", "/*", "*/", [new('(', ')')]);

    [Test]
    public static async Task AThirdPartyLanguageUsesThePublicKit()
    {
        var states = new LineStates(capacity: 1 << 16);
        var language = new LineStateLanguage("stanza-test", "A test-only language on the public kit", [".stanza"], StanzaRules, new StanzaTokenizer("key", states), new StanzaFormatter());
        EditorLanguages.Register(language);
        try
        {
            Check.True(ReferenceEquals(language, EditorLanguages.ForFile("a.stanza")), "a third party's line-state language registers and opens its files");
        }
        finally
        {
            EditorLanguages.Unregister(language.Name);
        }

        const string text = "key a << b\nc [[ d\n e ]] >> key\n/* x\ny */ key\n";
        var spans = Classify(language, text);
        Expect(spans, text, 0, "key", TokenClasses.Keyword, "a keyword at the top level");
        Expect(spans, text, text.IndexOf('b', StringComparison.Ordinal), "b", TokenClasses.String, "a word inside a frame");
        Expect(spans, text, text.IndexOf("c [[", StringComparison.Ordinal), "c", TokenClasses.String, "a frame opened on the line above carries on (its state interned)");
        Expect(spans, text, text.IndexOf(" e", StringComparison.Ordinal) + 1, "e", TokenClasses.String, "two frames deep");
        Expect(spans, text, text.IndexOf(">> key", StringComparison.Ordinal) + 3, "key", TokenClasses.Keyword, "both frames closed: a keyword again");
        Expect(spans, text, text.IndexOf("y */", StringComparison.Ordinal), "y */", TokenClasses.Comment, "a comment over two lines");
        Check.True(states.Count >= 2, $"the stacks were interned ({states.Count})");

        // Incremental, and the same as a fresh session after every random edit (rule 71).
        var lines = new List<string>();
        for (int n = 0; n < 2000; n++)
        {
            lines.Add(n % 5 == 0 ? $"key {n} << nested [[ deep" : n % 5 == 4 ? "]] >> key" : $"word{n} key");
        }

        var buffer = new PieceTableBuffer(string.Join('\n', lines) + "\n");
        using var session = (LineStateSession)language.Open(buffer);
        using var into = new TokenSpanBuffer();
        var last = buffer.Current.GetLine(buffer.Current.LineCount - 2);
        session.Classify(buffer.Current, last.Start, last.Length, into);
        long scanned = session.LinesScanned;
        buffer.Apply([TextEdit.Insert(buffer.Current.GetLine(11).Start + 2, "x")]);
        var line11 = buffer.Current.GetLine(11);
        into.Clear();
        session.Classify(buffer.Current, line11.Start, line11.Length, into);
        Check.True(session.KnownLines >= buffer.Current.LineCount - 1 && session.LinesScanned - scanned <= 1,
            $"typing inside a frame settles on the line below ({session.KnownLines} known, {session.LinesScanned - scanned} lines read again)");

        var random = new Random(23);
        string[] fragments = ["<<", ">>", "[[", "]]", "/*", "*/", "\n", "key ", "x", " ", "<", "]"];
        var edited = new PieceTableBuffer("key << a [[ b\n c ]] >>\n/* d */ key\n");
        using var live = language.Open(edited);
        for (int round = 0; round < 400; round++)
        {
            int length = edited.Current.Length;
            int start = random.Next(0, length + 1);
            int remove = random.Next(4) == 0 ? random.Next(0, Math.Min(length - start, 8) + 1) : 0;
            edited.Apply([new TextEdit(start, remove, fragments[random.Next(fragments.Length)])]);
            var shown = edited.Current.GetLine(random.Next(0, edited.Current.LineCount));
            into.Clear();
            live.Classify(edited.Current, shown.Start, shown.Length, into);
            if (round % 10 == 9)
            {
                var fresh = new PieceTableBuffer(edited.Current.GetText(0, edited.Current.Length));
                using var again = language.Open(fresh);
                using var expected = new TokenSpanBuffer();
                into.Clear();
                live.Classify(edited.Current, 0, edited.Current.Length, into);
                again.Classify(fresh.Current, 0, fresh.Current.Length, expected);
                Check.SequenceEqual(expected.Spans.ToArray(), into.Spans.ToArray(), $"round {round}: the spans equal a fresh session's");
            }
        }

        Check.Equal(0L, states.Overflows, "no stack overflowed the table in these edits");

        // A warm screen of 60 lines classifies with nothing allocated (rule 63), interned frames included.
        var screen = buffer.Current.GetLine(100);
        var screenEnd = buffer.Current.GetLine(159);
        for (int warm = 0; warm < 5; warm++)
        {
            into.Clear();
            session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 100; frame++)
        {
            into.Clear();
            session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
        }

        Check.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before, "100 warm screens of 60 lines allocate nothing");

        // Its formatter through the editor: one undo step, a malformed text left with the kit's message.
        var options = new CraftOptions { TabSize = 2, InsertSpaces = true };
        var (model, document, _) = Editors.Make("key   a  <<  b\n  c   >>\n", options, language);
        model.Dispatch = action => action();
        Check.True(model.FormattingSession is LineStateSession, "the session formats through the kit");
        Check.True(await model.FormatDocumentAsync(), "the format applied edits");
        Check.Equal("key a << b\n  c >>\n", document.Text, "only the white space between tokens changed");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("key   a  <<  b\n  c   >>\n", document.Text, "one undo takes it back");
        var (broken, brokenDocument, _) = Editors.Make("key\n  a ! b\n", options, language);
        broken.Dispatch = action => action();
        Check.True(!await broken.FormatDocumentAsync() && brokenDocument.Text == "key\n  a ! b\n", "a malformed text is left as it is");
        Check.True(broken.LastFormatMessage is { } message && message.Contains("line 2, column 5: a '!'", StringComparison.Ordinal), $"with ITextFormatter.Malformed's message ({broken.LastFormatMessage})");
        Check.Equal(new FormatStyle("  ", "\n"), new FormatStyle("  ", "\n"), "two styles of the same indent and break are equal");
        model.Dispose();
        broken.Dispose();
    }

    [Test]
    public static void TheCSharpPluginsLanguagesAreOnThePublicKit()
    {
        foreach (var language in ProjectLanguages.All)
        {
            Check.True(language is LineStateLanguage, $"{language.Name} is a public LineStateLanguage");
            using var session = language.Open(new PieceTableBuffer("x"));
            Check.True(session is LineStateSession, $"{language.Name}'s session is the public LineStateSession");
        }

        Check.True(ProjectLanguages.All.OfType<LineStateLanguage>().Single(l => l.Name == ProjectLanguages.Xml).Formats, "XML still formats");
        Check.True(!ProjectLanguages.All.OfType<LineStateLanguage>().Single(l => l.Name == ProjectLanguages.Razor).Formats, "Razor still does not");
    }

    [Test]
    public static void ASessionChangesItsDialectAndRaisesReclassified()
    {
        var states = new LineStates();
        var key = new StanzaTokenizer("key", states);
        var kez = new StanzaTokenizer("kez", states);
        var language = new DialectLanguage(key, kez);
        const string text = "key kez\n<< key\n>> kez key\n";
        var buffer = new PieceTableBuffer(text);
        using var session = (LineStateSession)language.Open(buffer);
        int raised = 0;
        session.Reclassified += (_, _) => raised++;
        using var into = new TokenSpanBuffer();
        session.Classify(buffer.Current, 0, buffer.Current.Length, into);
        Check.Equal(TokenClasses.Keyword, ClassAt(into, 0), "\"key\" is a keyword in the first dialect");
        Check.True(session.KnownLines >= 3, "the states were learned");

        Check.True(session.ChangeDialect(kez), "a new dialect is taken");
        Check.Equal(1, raised, "Reclassified is raised once");
        Check.Equal(0, session.KnownLines, "the states kept were dropped");
        Check.True(ReferenceEquals(kez, session.Tokenizer), "the session classifies with the new dialect");
        into.Clear();
        session.Classify(buffer.Current, 0, buffer.Current.Length, into);
        Check.Equal(TokenClasses.Identifier, ClassAt(into, 0), "\"key\" is plain in the second dialect");
        Check.Equal(TokenClasses.Keyword, ClassAt(into, 4), "\"kez\" is its keyword");
        using var expected = new TokenSpanBuffer();
        var freshBuffer = new PieceTableBuffer(text);
        using var freshSession = new LineStateSession(freshBuffer, kez, null, LanguageSettings.Default);
        freshSession.Classify(freshBuffer.Current, 0, freshBuffer.Current.Length, expected);
        Check.SequenceEqual(expected.Spans.ToArray(), into.Spans.ToArray(), "the spans equal a fresh session's of that dialect");

        Check.True(!session.ChangeDialect(kez) && raised == 1, "the same dialect again changes nothing and raises nothing");

        // Through the editor: the model passes it on (its paint redraws), and a file's dialect is the language's pick.
        var (model, _, _) = Editors.Make(text, null, language, path: "/w/a.stanza");
        int redraws = 0;
        model.Reclassified += (_, _) => redraws++;
        Check.True(((LineStateSession)model.LanguageSession!).ChangeDialect(kez) && redraws == 1, "the editor's Reclassified follows the session's");
        model.Dispose();
        using var picked = (LineStateSession)language.Open(new PieceTableBuffer(text), new LanguageOpenContext("/w/b.stanza2", null, LanguageSettings.Default));
        Check.True(ReferenceEquals(kez, picked.Tokenizer), "a LineStateLanguage subclass picks the dialect per file (TokenizerFor)");

        session.Dispose();
        Check.True(!session.ChangeDialect(key) && raised == 1, "a disposed session takes no dialect");
    }

    [Test]
    public static void InterningIsBoundedWithAResetFallback()
    {
        var table = new LineStates(capacity: 4, maxDepth: 3, fallback: 7);
        ulong a = table.Intern([1, 2]);
        ulong b = table.Intern([2, 1]);
        Check.True(LineStates.IsInterned(a) && LineStates.IsInterned(b) && a != b, "two stacks get two interned states");
        Check.Equal(a, table.Intern([1, 2]), "the same stack gets the same state");
        Check.SequenceEqual(new ulong[] { 2, 1 }, table.Frames(b).ToArray(), "a state reads back its frames");
        Check.True(!LineStates.IsInterned(7), "a plain state is not interned");
        table.Intern([1]);
        table.Intern([]);
        Check.Equal(4, table.Count, "four stacks held");
        Check.True(!table.TryIntern([3, 3], out ulong full) && full == 7, "the table full, a new stack gets the fallback (the reset state)");
        Check.Equal(7UL, table.Intern([1, 2, 3, 4]), "a stack deeper than the limit gets it too");
        Check.Equal(2L, table.Overflows, "both are counted");
        Check.Equal(a, table.Intern([1, 2]), "a stack held is still found when the table is full");
        Check.Throws<ArgumentException>(() => table.Frames(InternedBitOf(99)), "a state the table never gave is refused");
        Check.Throws<ArgumentOutOfRangeException>(() => _ = new LineStates(capacity: 0), "a table holds at least one stack");

        for (int warm = 0; warm < 3; warm++)
        {
            table.Intern([1, 2]);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            table.Intern([1, 2]);
            _ = table.Frames(a);
        }

        Check.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before, "looking up a held stack and reading its frames allocate nothing");

        // Memory does not grow with use (rule 77): a tokenizer whose table is full resumes from the reset state.
        var small = new LineStates(capacity: 2);
        var language = new LineStateLanguage("stanza-small", "", [".s2"], StanzaRules, new StanzaTokenizer("key", small));
        var text = string.Join('\n', Enumerable.Range(0, 1000).Select(n => n % 2 == 0 ? $"<< w{n}" : $"[[ w{n}")) + "\nkey\n";
        var spans = Classify(language, text);
        Check.Equal(2, small.Count, "a thousand distinct stacks, two held");
        Check.True(small.Overflows > 0, $"the rest overflowed ({small.Overflows})");
        Expect(spans, text, text.LastIndexOf("key", StringComparison.Ordinal), "key", TokenClasses.Keyword,
            "past the bound the state resets to the top level (the fallback) instead of growing the table");
    }

    [Test]
    public static void RulesAtAndToggleCommentsBlockFallback()
    {
        var language = new LineStateLanguage("stanza-rules", "", [".s3"], StanzaRules, new StanzaTokenizer("key", new LineStates()));

        // A language with only a block comment (CSS, HTML): Ctrl+/ wraps the line, again unwraps it.
        var (model, document, _) = Editors.MakeMarked("a {\n  b: |c;\n}\n", null, language);
        Check.True(model.ToggleLineComment(), "toggle comment works with a block comment only");
        Check.Equal("a {\n  /* b: c; */\n}\n", document.Text, "the line's text wrapped, its indent kept");
        Check.Equal(document.Text.IndexOf("c;", StringComparison.Ordinal), model.Selection.Caret, "the caret stays on its character");
        Check.True(model.ToggleLineComment(), "again");
        Check.Equal("a {\n  b: c;\n}\n", document.Text, "unwrapped");

        var (block, blockDocument, _) = Editors.Make("a {\n  b: c;\n}\n", null, language);
        block.Select(0, blockDocument.Text.IndexOf('}', StringComparison.Ordinal) + 1);
        int steps = blockDocument.Kinds.Count;
        block.ToggleLineComment();
        Check.Equal("/* a {\n  b: c;\n} */\n", blockDocument.Text, "several lines: one pair, from the first line's indent to the last line's end");
        Check.Equal(steps + 1, blockDocument.Kinds.Count, "one undo step");
        Check.True(block.UncommentLines() && blockDocument.Text == "a {\n  b: c;\n}\n", "Ctrl+K Ctrl+U takes it off");
        Check.True(block.CommentLines() && blockDocument.Text.StartsWith("/* a {", StringComparison.Ordinal), "Ctrl+K Ctrl+C puts it on");
        block.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("a {\n  b: c;\n}\n", blockDocument.Text, "undo takes the comment back");

        // XML (the C# plug-in's markup: "<!-- -->" only) gets the same fallback.
        var xml = ProjectLanguages.All.First(l => l.Name == ProjectLanguages.Xml);
        var (markup, markupDocument, _) = Editors.MakeMarked("<a>\n  <b|>x</b>\n</a>\n", null, xml);
        markup.ToggleLineComment();
        Check.Equal("<a>\n  <!-- <b>x</b> -->\n</a>\n", markupDocument.Text, "an XML line is wrapped in <!-- -->");

        var (plain, plainDocument, _) = Editors.MakeMarked("te|xt", null, null);
        Check.True(!plain.ToggleLineComment() && plainDocument.Text == "text", "plain text has no comment: nothing done");

        // RulesAt: inside a `[[` part the session answers the embedded rules (as <script> will answer `//`).
        const string text = "key [[\n  x y\n]]\nz\n";
        var (embedded, embeddedDocument, _) = Editors.Make(text, null, language);
        int inside = text.IndexOf('x', StringComparison.Ordinal);
        Check.Equal("//", embedded.RulesAt(inside).LineComment, "inside the part, the part's rules");
        Check.True(ReferenceEquals(language.Rules, embedded.RulesAt(text.IndexOf('z', StringComparison.Ordinal))), "outside it, the language's own");
        Check.True(embedded.LanguageSession!.RulesAt(embedded.Snapshot, 0) is null, "the session's null means the language's rules");
        using (var wrapper = new SyntacticLanguageSession(language.Open(new PieceTableBuffer(text))))
        {
            Check.Equal("//", wrapper.RulesAt(new PieceTableBuffer(text).Current, inside)?.LineComment, "a session over an older or other snapshot answers from the top too");
        }

        embedded.Select(inside, inside);
        embedded.ToggleLineComment();
        Check.Equal("key [[\n  // x y\n]]\nz\n", embeddedDocument.Text, "toggle comment inside the part uses its line comment");
        int z = embeddedDocument.Text.IndexOf('z', StringComparison.Ordinal);
        embedded.Select(z, z);
        embedded.ToggleLineComment();
        Check.Equal("key [[\n  // x y\n]]\n/* z */\n", embeddedDocument.Text, "outside it, the language's block comment");

        var (quote, _, _) = Editors.Make("x", null, new QuoteLanguage());
        Check.True(ReferenceEquals(quote.Rules, quote.RulesAt(0)), "a session that does not answer RulesAt (the default) gives the language's rules");
        model.Dispose();
        block.Dispose();
        markup.Dispose();
        plain.Dispose();
        embedded.Dispose();
        quote.Dispose();
    }

    private static ulong InternedBitOf(int id) => LineStates.InternedBit | (uint)id;

    private static string? ClassAt(TokenSpanBuffer spans, int start) => spans.Spans.ToArray().FirstOrDefault(s => s.Start == start).Class;

    private static List<TokenSpan> Classify(LineStateLanguage language, string text)
    {
        var buffer = new PieceTableBuffer(text);
        using var session = language.Open(buffer);
        using var spans = new TokenSpanBuffer();
        session.Classify(buffer.Current, 0, buffer.Current.Length, spans);
        return [.. spans.Spans.ToArray()];
    }

    private static void Expect(List<TokenSpan> spans, string text, int start, string token, string tokenClass, string what)
    {
        Check.True(spans.Any(s => s.Start == start && s.Length == token.Length && s.Class == tokenClass),
            $"{what}: \"{token}\" at {start} is {tokenClass} (found: {string.Join(", ", spans.Where(s => s.Start == start).Select(s => $"{text.Substring(s.Start, s.Length)}={s.Class}").DefaultIfEmpty("no span"))})");
    }

    /// <summary>A LineStateLanguage that picks its dialect by file: ".stanza2" lexes with the second tokenizer.</summary>
    private sealed class DialectLanguage(LineTokenizer first, LineTokenizer second)
        : LineStateLanguage("stanza-dialects", "", [".stanza", ".stanza2"], StanzaRules, first)
    {
        protected override LineTokenizer TokenizerFor(LanguageOpenContext context) =>
            context.FilePath?.EndsWith(".stanza2", StringComparison.OrdinalIgnoreCase) == true ? second : Tokenizer;
    }

    /// <summary>The test-only tokenizer, written against the public kit only (as a third party's would be).</summary>
    private sealed class StanzaTokenizer(string keyword, LineStates states) : LineTokenizer
    {
        private const ulong InComment = 1;
        private const int MaxFrames = 64;

        public override ulong Tokenize(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into) =>
            Walk(line, lineStart, state, into, line.Length, out _);

        public override LanguageRules? RulesAt(ReadOnlySpan<char> line, int column, ulong state)
        {
            Walk(line, 0, state, null, Math.Min(column, line.Length), out ulong top);
            return top == 2 ? ScriptRules : null;
        }

        private ulong Walk(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into, int upTo, out ulong top)
        {
            Span<ulong> stack = stackalloc ulong[MaxFrames];
            int depth = 0;
            bool comment = state == InComment;
            if (LineStates.IsInterned(state))
            {
                var frames = states.Frames(state);
                frames.CopyTo(stack);
                depth = frames.Length;
            }

            int i = 0;
            while (i < upTo)
            {
                if (comment)
                {
                    int close = line[i..].IndexOf("*/", StringComparison.Ordinal);
                    int length = close < 0 ? line.Length - i : close + 2;
                    Add(into, lineStart + i, length, TokenClasses.Comment);
                    i += length;
                    comment = close < 0;
                    continue;
                }

                char c = line[i];
                char next = i + 1 < line.Length ? line[i + 1] : '\0';
                if (depth == 0 && c == '/' && next == '*')
                {
                    int close = line[(i + 2)..].IndexOf("*/", StringComparison.Ordinal);
                    int length = close < 0 ? line.Length - i : close + 4;
                    Add(into, lineStart + i, length, TokenClasses.Comment);
                    i += length;
                    comment = close < 0;
                }
                else if ((c == '<' && next == '<') || (c == '[' && next == '['))
                {
                    if (depth < MaxFrames)
                    {
                        stack[depth++] = c == '<' ? 1UL : 2UL;
                    }

                    Add(into, lineStart + i, 2, TokenClasses.Punctuation);
                    i += 2;
                }
                else if (((c == '>' && next == '>') || (c == ']' && next == ']')) && depth > 0 && stack[depth - 1] == (c == '>' ? 1UL : 2UL))
                {
                    depth--;
                    Add(into, lineStart + i, 2, TokenClasses.Punctuation);
                    i += 2;
                }
                else if (char.IsAsciiLetterOrDigit(c))
                {
                    int j = i + 1;
                    while (j < line.Length && char.IsAsciiLetterOrDigit(line[j]))
                    {
                        j++;
                    }

                    string tokenClass = depth > 0 ? TokenClasses.String : line[i..j].SequenceEqual(keyword) ? TokenClasses.Keyword : TokenClasses.Identifier;
                    Add(into, lineStart + i, j - i, tokenClass);
                    i = j;
                }
                else
                {
                    i++;
                }
            }

            top = depth > 0 ? stack[depth - 1] : 0;
            return comment ? InComment : depth == 0 ? 0 : states.Intern(stack[..depth]);
        }
    }

    /// <summary>The test-only formatter: one space between two tokens of a line; a '!' is malformed.</summary>
    private sealed class StanzaFormatter : ITextFormatter
    {
        public void Format(ReadOnlySpan<char> text, FormatStyle style, List<TextEdit> edits)
        {
            int bang = text.IndexOf('!');
            if (bang >= 0)
            {
                throw ITextFormatter.Malformed(text, bang, "a '!'");
            }

            int lineStart = 0;
            while (lineStart < text.Length)
            {
                int end = text[lineStart..].IndexOf('\n');
                end = end < 0 ? text.Length : lineStart + end;
                int i = lineStart;
                while (i < end && text[i] == ' ')
                {
                    i++;
                }

                while (i < end)
                {
                    while (i < end && text[i] != ' ')
                    {
                        i++;
                    }

                    int gap = i;
                    while (i < end && text[i] == ' ')
                    {
                        i++;
                    }

                    if (i < end && i - gap > 1)
                    {
                        edits.Add(new TextEdit(gap, i - gap, " "));
                    }
                }

                lineStart = end + 1;
            }
        }
    }
}
