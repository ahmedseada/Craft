// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;
using Craft.Editor;
using Craft.Tests.Editor;
using Craft.Tests.Runner;
using Craft.Web.Markup;
using Craft.Web.Script;
using Craft.Web.Style;
using static Craft.Tests.ProjectLanguageTests;

namespace Craft.Tests;

/// <summary>
/// Phase W's W-markup, HTML: the living standard's colours, the embedded JavaScript and CSS equal to their own sessions'
/// (shifted), a script ended by its end tag even inside a string, script types, random edits equal to a fresh session, a warm
/// screen at 0 bytes, a keystroke and the worst flip, the built-in formatter (exact output, idempotent, embedded script and
/// style formatted inside their element, &lt;pre&gt; kept, malformed left with where, the caret kept, one undo step, Format
/// Selection), folding and the rules inside &lt;script&gt; and &lt;style&gt;. Numbers in test-output/benches/web-markup.txt.
/// </summary>
[TestGroup("WebMarkup")]
public static class WebMarkupTests
{
    internal const string Page = """
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <title>Shop &amp; more</title>
          <style>
            body { color: red; }
            /* a comment
               over lines */
            .a { margin: 0; }
          </style>
          <script type="importmap">{ "imports": { "a": "./a.js" } }</script>
        </head>
        <body>
          <!-- a comment -->
          <my-widget data-x='1' hidden></my-widget>
          <button onclick="go(1)" style="color: blue; margin: 0">Go &rarr;</button>
          <textarea><b>not a tag</b></textarea>
          <script type="module">
            const s = `a
              ${b}`;
            /* over
               lines */
            if (x) /re/.test(s);
          </script>
          <template><p>inside</p></template>
        </body>
        </html>
        """;

    [Test]
    public static void HtmlHasItsColours()
    {
        var spans = Spans(HtmlTokenizer.For(HtmlOptions.Html), Page);
        Expect(spans, "<!DOCTYPE", TokenClasses.PreprocessorKeyword, "the doctype");
        Expect(spans, " html>", TokenClasses.PreprocessorText, "the doctype's text");
        Expect(spans, "html", TokenClasses.Keyword, "a tag");
        Expect(spans, "lang", TokenClasses.PropertyName, "an attribute");
        Expect(spans, "\"en\"", TokenClasses.String, "a value");
        Expect(spans, "&amp;", TokenClasses.StringEscape, "an entity in RCDATA (title)");
        Expect(spans, "color", TokenClasses.PropertyName, "a <style>'s CSS property");
        Expect(spans, "\"imports\"", TokenClasses.String, "an importmap read JSON-like");
        Expect(spans, "<!-- a comment -->", TokenClasses.Comment, "a comment");
        Expect(spans, "my-widget", TokenClasses.ClassName, "a custom element");
        Expect(spans, "data-x", TokenClasses.PropertyName, "a data attribute");
        Expect(spans, "'1'", TokenClasses.String, "a single-quoted value");
        Expect(spans, "hidden", TokenClasses.PropertyName, "an attribute with no value");
        Expect(spans, "go", TokenClasses.MethodName, "onclick=\"…\" as JavaScript");
        Expect(spans, "&rarr;", TokenClasses.StringEscape, "an entity in text");
        Expect(spans, "const", TokenClasses.Keyword, "a module script's JavaScript");
        Expect(spans, "/re/", TokenClasses.String, "a regex in the script (its state across lines)");
        Expect(spans, "p", TokenClasses.Keyword, "inside <template>");
        Check.True(!spans.Any(s => s.Text == "b" && s.Class == TokenClasses.Keyword), "a <textarea>'s content is text, not tags");
        Check.True(spans.Count(s => s.Text == "margin" && s.Class == TokenClasses.PropertyName) == 2, "style=\"…\" as CSS declarations too");

        var plain = Spans(HtmlTokenizer.For(HtmlOptions.Html with { Embedded = false, AttributesAsCode = false }), Page);
        Check.True(!plain.Any(s => s.Text == "const" || s.Text == "go" || (s.Text == "color" && s.Class == TokenClasses.PropertyName)), "with the embedded languages and attributes as code off, they are plain");
    }

    [Test]
    public static void EmbeddedSpansEqualTheirOwnSessionsShifted()
    {
        var html = HtmlTokenizer.For(HtmlOptions.Html);
        const string script = "const s = `a\n  ${b + `c`}\n`;\n/* over\n   lines */\nif (x) /re/g.test(s);\nlet y = a / 2;";
        const string style = "body { color: red; }\n/* a\n   b */\n.a:hover { margin: 0 4px; --x: oklch(0.5 0.1 200); }";
        string text = "<p>x</p>\n<script>\n" + script + "\n</script>\n<style>\n" + style + "\n</style>\n";
        var all = SpansAt(html, text);
        int scriptStart = text.IndexOf("<script>\n", StringComparison.Ordinal) + 9;
        int styleStart = text.IndexOf("<style>\n", StringComparison.Ordinal) + 8;
        var inScript = all.Where(s => s.Start >= scriptStart && s.End <= scriptStart + script.Length).ToArray();
        var alone = SpansAt(html.JavaScript, script).Select(s => s with { Start = s.Start + scriptStart }).ToArray();
        Check.SequenceEqual(alone, inScript, "the script's spans equal a JavaScript session's over its text, shifted");
        var inStyle = all.Where(s => s.Start >= styleStart && s.End <= styleStart + style.Length).ToArray();
        var styleAlone = SpansAt(StyleTokenizer.For(StyleDialect.Css), style).Select(s => s with { Start = s.Start + styleStart }).ToArray();
        Check.SequenceEqual(styleAlone, inStyle, "the style's spans equal a CSS session's, shifted");
    }

    [Test]
    public static void AScriptEndsAtItsEndTagEvenInsideAStringAndTypesChooseItsLanguage()
    {
        const string text = "<script>\nconst s = \"</script>\";\nconst t = \"<\\/script>\";\n</script>\n<p>after</p>";
        var spans = SpansAt(HtmlTokenizer.For(HtmlOptions.Html), text);
        int inString = text.IndexOf("</script>\"", StringComparison.Ordinal) + 2;
        Check.True(spans.Any(s => s.Start == inString && s.Class == TokenClasses.Keyword), "</script inside a string ends the script, as the browser does (the living standard)");
        int after = text.IndexOf("after", StringComparison.Ordinal) - 2;
        Check.True(spans.Any(s => s.Start == after && s.Class == TokenClasses.Keyword), "the lexer reads on: <p> after it is a tag");
        var error = Check.Throws<FormatException>(() => Format(new HtmlFormatter(HtmlTokenizer.For(HtmlOptions.Html)), text, new FormatStyle("  ", "\n")), "the stray end tag is malformed for the formatter");
        Check.True(error.Message.Contains("closes nothing open", StringComparison.Ordinal), $"said ({error.Message})");
        var escaped = Spans(HtmlTokenizer.For(HtmlOptions.Html), "<script>\nconst t = \"<\\/script>\";\nconst u = 1;\n</script>");
        Check.True(escaped.Count(s => s.Text == "const" && s.Class == TokenClasses.Keyword) == 2, "<\\/script> does not end it");

        var types = Spans(HtmlTokenizer.For(HtmlOptions.Html),
            "<script type=\"text/ng-template\"><div class=\"a\">x</div></script>\n<script type=\"application/ld+json\">{\"a\": 1}</script>\n<script type=\"text/plain\">const x</script>\n<script>let y;</script>");
        Expect(types, "div", TokenClasses.Keyword, "a text/ng-template script reads HTML");
        Expect(types, "\"a\"", TokenClasses.String, "a JSON type reads JSON-like");
        Expect(types, "1", TokenClasses.Number, "its number");
        Check.True(!types.Any(s => s.Text == "const"), "a text/plain script is plain");
        Expect(types, "let", TokenClasses.Keyword, "a script with no type is JavaScript");
    }

    [Test]
    public static void RandomEditsEqualAFreshSession()
    {
        string[] fragments = ["<", ">", "</", "\"", "'", "\n", "\n", "\n", "<script>", "</script>", "<style>", "</style>", "{{", "}}", "@if (x) {", "}", "<!--", "-->", "=", "`", "/*", "*/", " ", "<p>", "</p>", "&amp;"];
        int problems = 0;
        foreach (var (options, seed, fixture) in new[] { (HtmlOptions.Html, 5, Page), (HtmlOptions.AngularTemplate, 9, WebAngularTests.Template + Page) })
        {
            var tokenizer = new HtmlTokenizer(options, new LineStates(capacity: 1 << 16));
            var random = new Random(seed);
            var buffer = new PieceTableBuffer(fixture);
            using var live = new LineStateSession(buffer, tokenizer, null, LanguageSettings.Default);
            using var into = new TokenSpanBuffer();
            for (int round = 0; round < 1500; round++)
            {
                int length = buffer.Current.Length;
                int start = random.Next(0, length + 1);
                int remove = random.Next(4) == 0 ? random.Next(0, Math.Min(length - start, 6) + 1) : 0;
                buffer.Apply([new TextEdit(start, remove, fragments[random.Next(fragments.Length)])]);
                var shown = buffer.Current.GetLine(random.Next(0, buffer.Current.LineCount));
                into.Clear();
                live.Classify(buffer.Current, shown.Start, shown.Length, into);
                if (round % 25 == 24)
                {
                    var fresh = new PieceTableBuffer(buffer.Current.GetText(0, buffer.Current.Length));
                    using var again = new LineStateSession(fresh, tokenizer, null, LanguageSettings.Default);
                    using var expected = new TokenSpanBuffer();
                    into.Clear();
                    live.Classify(buffer.Current, 0, buffer.Current.Length, into);
                    again.Classify(fresh.Current, 0, fresh.Current.Length, expected);
                    if (!expected.Spans.SequenceEqual(into.Spans))
                    {
                        problems++;
                        Check.SequenceEqual(expected.Spans.ToArray(), into.Spans.ToArray(), $"{(options.Angular ? "Angular" : "HTML")}, round {round}: the spans equal a fresh session's");
                    }
                }
            }
        }

        Check.Equal(0, problems, "3,000 random edits (tags, quotes, raw text, comments, blocks, line breaks) equal a fresh session");
    }

    [Test]
    public static void AWarmScreenAllocatesNothingAKeystrokeSettlesAndTheNumbersAreWritten()
    {
        var tokenizer = new HtmlTokenizer(HtmlOptions.AngularTemplate, new LineStates());
        string text = Repeat(Page + "\n" + WebAngularTests.Template + "\n", 2000);
        var buffer = new PieceTableBuffer(text);
        using var session = new LineStateSession(buffer, tokenizer, null, LanguageSettings.Default);
        using var into = new TokenSpanBuffer();
        var screen = buffer.Current.GetLine(1000);
        var screenEnd = buffer.Current.GetLine(1059);
        long start = Stopwatch.GetTimestamp();
        session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
        double cold = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var last = buffer.Current.GetLine(buffer.Current.LineCount - 1);
        into.Clear();
        session.Classify(buffer.Current, last.Start, last.Length, into);
        for (int warm = 0; warm < 50; warm++)
        {
            into.Clear();
            session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
        }

        const int Frames = 500;
        long before = GC.GetAllocatedBytesForCurrentThread();
        start = Stopwatch.GetTimestamp();
        for (int frame = 0; frame < Frames; frame++)
        {
            into.Clear();
            session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
        }

        double warmScreen = Stopwatch.GetElapsedTime(start).TotalMilliseconds / Frames;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.Equal(0L, allocated, "500 warm screens of 60 lines (script, style, attributes, blocks interned) allocate nothing");

        // A letter typed in a tag's name re-reads its line; the states below settle.
        int lines = buffer.Current.LineCount;
        int at = -1;
        for (int index = 1000; index < 1060 && at < 0; index++)
        {
            var line = buffer.Current.GetLine(index);
            int name = buffer.Current.GetText(line.Start, line.Length).IndexOf("<button", StringComparison.Ordinal);
            at = name < 0 ? -1 : line.Start + name + 3;
        }

        Check.True(at >= 0, "a <button> is on the screen");
        long scanned = session.LinesScanned;
        buffer.Apply([TextEdit.Insert(at, "x")]);
        into.Clear();
        session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
        long perKeystroke = session.LinesScanned - scanned;
        Check.True(perKeystroke <= 1 && session.KnownLines >= lines - 1, $"a keystroke re-reads at most its line ({perKeystroke}), the rest settles ({session.KnownLines} of {lines})");

        // The worst flip: "<!--" typed at the top of a 20,000-line page comments everything below; only the screen is read.
        var page = new PieceTableBuffer(Repeat(Page + "\n", 20_000));
        using var flipSession = new LineStateSession(page, new HtmlTokenizer(HtmlOptions.Html, new LineStates()), null, LanguageSettings.Default);
        var bottom = page.Current.GetLine(page.Current.LineCount - 1);
        into.Clear();
        start = Stopwatch.GetTimestamp();
        flipSession.Classify(page.Current, bottom.Start, bottom.Length, into);
        double fullRead = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long flipBefore = flipSession.LinesScanned;
        start = Stopwatch.GetTimestamp();
        page.Apply([TextEdit.Insert(page.Current.GetLine(1).Start, "<!--")]);
        var pageScreenEnd = page.Current.GetLine(59);
        into.Clear();
        flipSession.Classify(page.Current, 0, pageScreenEnd.End, into);
        double flip = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long flipLines = flipSession.LinesScanned - flipBefore;
        Check.True(flipLines <= 60, $"the flip reads nothing beyond the screen ({flipLines} read; {flipSession.KnownLines} known, settled at the page's next comment)");

        var file = Repeat(Page + "\n", 2000);
        var formatter = new HtmlFormatter(HtmlTokenizer.For(HtmlOptions.Html));
        var edits = new List<TextEdit>();
        start = Stopwatch.GetTimestamp();
        formatter.Format(file, new FormatStyle("  ", "\n"), edits);
        double formatCold = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        edits.Clear();
        start = Stopwatch.GetTimestamp();
        formatter.Format(file, new FormatStyle("  ", "\n"), edits);
        double formatWarm = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        var ranges = HtmlFolding.Ranges(HtmlTokenizer.For(HtmlOptions.Html), new PieceTableBuffer(file).Current);
        double folding = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        string report = string.Create(CultureInfo.InvariantCulture, $"""
            W-markup bench (test WebMarkup.AWarmScreenAllocatesNothingAKeystrokeSettlesAndTheNumbersAreWritten), {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC, {Environment.ProcessorCount} cores, .NET {Environment.Version}
            {lines:N0}-line Angular template with HTML pages (script, style, attributes, blocks):
              a 60-line screen at line 1,000, cold (1,000 states learned):  {cold,8:F3} ms
              a warm 60-line screen (mean of {Frames}):                        {warmScreen,8:F4} ms, 0 bytes
              a letter typed in a tag's name:                               {perKeystroke} lines read, the rest settles
            {page.Current.LineCount:N0}-line HTML page:
              the whole file's states, first read:                          {fullRead,8:F1} ms
              "<!--" typed on line 1, then the screen:                      {flip,8:F3} ms, {flipLines} lines read beyond the screen
            {file.Count(c => c == '\n'):N0}-line HTML page:
              Format document, cold:                                        {formatCold,8:F1} ms
              Format document, warm:                                        {formatWarm,8:F1} ms ({edits.Count} edits)
              folding:                                                      {folding,8:F1} ms ({ranges.Count} ranges)

            """);
        string folder = TestRunner.RepositoryPath("test-output", "benches");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "web-markup.txt"), report);
        Console.WriteLine(report);
    }

    [Test]
    public static void TheFormatterIndentsByElementsAndFormatsEmbeddedCode()
    {
        var formatter = new HtmlFormatter(HtmlTokenizer.For(HtmlOptions.Html));
        var style = new FormatStyle("  ", "\n");
        const string messy = "<!DOCTYPE html>\n<html>\n<head>\n<title>T</title>\n<style>\nbody{color:red}\n</style>\n</head>\n<body>\n<ul>\n<li>one\n<li>two</li>\n</ul>\n<p>Some   <b>bold</b>   text\n<div   class = \"a\"   id=\"b\"  >\n<img src=\"x.png\" >\n<input type=\"text\"/>\n</div>\n<pre>\n  keep   this\n</pre>\n<script>\nif(x){go()}\n</script>\n\n\n\n<button\nclass=\"c\"\n>Go</button>\n</body>\n</html>\n";
        string css = Format(new StyleFormatter(StyleDialect.Css), "body{color:red}", style);
        string cssBlock = string.Join("\n", css.Split('\n').Select(l => l.Length == 0 ? l : "      " + l));
        string formatted = "<!DOCTYPE html>\n<html>\n  <head>\n    <title>T</title>\n    <style>\n" + cssBlock + "\n    </style>\n  </head>\n  <body>\n    <ul>\n      <li>one\n      <li>two</li>\n    </ul>\n    <p>Some   <b>bold</b>   text\n    <div class=\"a\" id=\"b\">\n      <img src=\"x.png\">\n      <input type=\"text\" />\n    </div>\n    <pre>\n  keep   this\n</pre>\n    <script>\n      if (x) { go() }\n    </script>\n\n    <button\n      class=\"c\"\n    >Go</button>\n  </body>\n</html>\n";
        Check.Equal(formatted, Format(formatter, messy, style), "indented by elements (an <li> and a <div> closing what they imply), tags' spaces made one, <pre> kept, the script and the style formatted inside their element, one empty line kept");
        Check.Equal(formatted, Format(formatter, formatted, style), "formatting again changes nothing");
        foreach (var text in new[] { messy, Page })
        {
            string once = Format(formatter, text, style);
            Check.Equal(Strip(text), Strip(once), "only white space changed");
            Check.Equal(once, Format(formatter, once, style), "idempotent");
        }

        Check.Equal("<div>\n  <script>\n    if (x {\n  </script>\n</div>", Format(formatter, "<div>\n<script>\n    if (x {\n  </script>\n</div>", style),
            "a script that does not parse is kept as written (its element still indented)");

        foreach (var (bad, where, what) in new[]
        {
            ("<div><span></div>", "line 1, column 12", "</div> where <span> is not closed"),
            ("<section>\n  <p>x</p>\n", "line 1, column 1", "<section> is not closed"),
            ("<p>x</div>", "line 1, column 5", "closes nothing open"),
            ("<p>x</p>\n<!-- open\n", "line 2, column 1", "the comment is not closed"),
            ("<a href=\"x>\n", "line 1, column 1", "the tag is not closed"),
        })
        {
            var error = Check.Throws<FormatException>(() => Format(formatter, bad, style), $"malformed is refused: {bad.Replace("\n", "\\n", StringComparison.Ordinal)}");
            Check.True(error.Message.StartsWith(where, StringComparison.Ordinal) && error.Message.Contains(what, StringComparison.Ordinal), $"the message says where and what ({error.Message})");
        }
    }

    [Test]
    public static async Task FormatKeepsTheCaretIsOneUndoStepAndFormatsASelection()
    {
        var language = new HtmlLanguage("html-format-test", [".html"]);
        var options = new CraftOptions { TabSize = 2, InsertSpaces = true };
        var (model, document, _) = Editors.MakeMarked("<div   class = \"a\">|text</div>\n", options, language);
        model.Dispatch = action => action();
        int steps = document.Kinds.Count;
        Check.True(model.FormattingSession is LineStateSession, "HTML formats through the public kit's session");
        Check.True(await model.FormatDocumentAsync(), "the format applied edits");
        Check.Equal("<div class=\"a\">text</div>\n", document.Text, "formatted");
        Check.Equal(document.Text.IndexOf("text", StringComparison.Ordinal), model.Selection.Caret, "the caret kept its place");
        Check.Equal(steps + 1, document.Kinds.Count, "one step");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("<div   class = \"a\">text</div>\n", document.Text, "one undo takes it back");

        var (broken, brokenDocument, _) = Editors.Make("<div>\n  <span>\n</div>\n", options, language);
        broken.Dispatch = action => action();
        Check.True(!await broken.FormatDocumentAsync() && brokenDocument.Text == "<div>\n  <span>\n</div>\n", "a malformed file is left unchanged");
        Check.True(broken.LastFormatMessage is { } message && message.Contains("line 3, column 1", StringComparison.Ordinal), $"with the reason ({broken.LastFormatMessage})");

        const string text = "<p  id=\"a\">x</p>\n<p  id=\"b\">y</p>\n";
        var buffer = new PieceTableBuffer(text);
        using var session = (LineStateSession)language.Open(buffer);
        int second = text.IndexOf("<p  id=\"b\"", StringComparison.Ordinal);
        var inside = await session.FormatAsync(buffer.Current, second, 16, CancellationToken.None);
        Check.True(inside.Count > 0 && inside.All(e => e.Start >= second && e.End <= second + 16), "Format Selection edits only the selected line");
        buffer.Apply([.. inside]);
        Check.Equal("<p  id=\"a\">x</p>\n<p id=\"b\">y</p>\n", buffer.Current.GetText(0, buffer.Current.Length), "the line around it is left");
        model.Dispose();
        broken.Dispose();
    }

    [Test]
    public static void FoldingCoversElementsCommentsRegionsAndRules()
    {
        const string text = "<html>\n<body>\n  <ul>\n    <li>a\n    <li>b\n  </ul>\n  <!-- a\n    comment -->\n  <!-- #region x -->\n  <img\n    src=\"a\">\n  <!-- #endregion -->\n</body>\n</html>\n";
        var ranges = HtmlFolding.Ranges(HtmlTokenizer.For(HtmlOptions.Html), new PieceTableBuffer(text).Current);
        FoldingRange[] expected =
        [
            new(0, 13),
            new(1, 12),
            new(2, 5),
            new(6, 7, FoldingRangeKind.Comment),
            new(8, 11, FoldingRangeKind.Marked),
            new(9, 10),
        ];
        Check.SequenceEqual(expected, ranges.OrderBy(r => r.StartLine).ThenByDescending(r => r.EndLine).ToArray(),
            "elements (an <li> closed by the next ends with it), a comment, a region, a start tag over lines; void elements never");

        const string page = "<p>x</p>\n<script>\n  let a = 1;\n</script>\n<style>\n  p { color: red; }\n</style>\n";
        var buffer = new PieceTableBuffer(page);
        using var session = new LineStateSession(buffer, HtmlTokenizer.For(HtmlOptions.Html), null, LanguageSettings.Default);
        Check.True(session.RulesAt(buffer.Current, page.IndexOf("let", StringComparison.Ordinal)) is { LineComment: "//" }, "inside <script>, // comments");
        Check.True(session.RulesAt(buffer.Current, page.IndexOf("p {", StringComparison.Ordinal)) is { LineComment: null, BlockCommentOpen: "/*" }, "inside <style>, /* */");
        Check.True(session.RulesAt(buffer.Current, 0) is null, "elsewhere HTML's own (<!-- -->)");
        Check.True(HtmlTokenizer.HtmlRules is { LineComment: null, BlockCommentOpen: "<!--", BlockCommentClose: "-->" }, "HTML's rules");
    }

    // ---- helpers ----

    internal static List<(string Text, string Class)> Spans(LineTokenizer tokenizer, string text) =>
        [.. SpansAt(tokenizer, text).Select(s => (text.Substring(s.Start, s.Length), s.Class))];

    internal static List<TokenSpan> SpansAt(LineTokenizer tokenizer, string text)
    {
        var buffer = new PieceTableBuffer(text);
        using var session = new LineStateSession(buffer, tokenizer, null, LanguageSettings.Default);
        using var spans = new TokenSpanBuffer();
        session.Classify(buffer.Current, 0, buffer.Current.Length, spans);
        var result = new List<TokenSpan>();
        int end = 0;
        foreach (var span in spans.Spans)
        {
            Check.True(span.Start >= end && span.Length > 0, $"the spans are in order, not empty and do not overlap (at {span.Start})");
            end = span.End;
            result.Add(span);
        }

        return result;
    }

    internal static string Strip(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    internal static string Repeat(string text, int lines)
    {
        var builder = new StringBuilder();
        int count = 0;
        while (count < lines)
        {
            builder.Append(text);
            count += text.Count(c => c == '\n') + 1;
            builder.Append('\n');
        }

        return builder.ToString();
    }
}
