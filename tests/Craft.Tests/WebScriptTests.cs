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
using Craft.Web.Script;
using static Craft.Tests.ProjectLanguageTests;

namespace Craft.Tests;

/// <summary>
/// Phase W's W-script: the JavaScript, JSX, TypeScript and TSX lexer of Craft.Web (the latest syntax of each, with expected
/// spans), regex or division by the token before, templates and JSX nested across lines, random edits equal to a fresh
/// session, nothing allocated on a warm screen, the lines read again per keystroke and for the worst flip (a backtick at
/// the top of a 100,000-line file), the version gate and the project's versions, the built-in formatter (idempotent,
/// malformed left alone, the caret kept, Format Selection, the line width), folding, the rules inside JSX and the Angular
/// expression stub. The numbers are written to test-output/benches/web-script.txt.
/// </summary>
[TestGroup("WebScript")]
public static class WebScriptTests
{
    private const string LatestJavaScript = """
        #!/usr/bin/env node
        /// <reference types="node" />
        import data from "./data.json" with { type: "json" };
        import defer * as heavy from "./heavy.js";
        import source wasm from "./mod.wasm";
        /**
         * Adds two numbers.
         * @param {number} a the first
         */
        @logged
        export class Counter extends Base {
          static #count = 0n;
          accessor value = 1_000_000;
          get total() { return #count in this ? this.#secret : 0xFF_FF; }
          async *items() { yield* [1, 2]; }
        }
        await using res = getResource();
        using lock = acquire();
        const re = /(?<year>\d{4})-(?i:abc)[\p{L}--[a-z]]/dv;
        const half = total / 2 / count;
        const tpl = `a ${b + `c ${d}`} e\n`;
        for (const x of xs) if (x) /x/.test(x);
        """;

    private const string LatestTypeScript = """
        import type { Foo } from "./foo";
        export type * from "./types";
        import fs = require("fs");
        declare module "x" { }
        declare global { interface Window { app: App } }
        namespace NS.Inner { }
        export abstract class Shape<const T, in out U> implements HasArea {
          private readonly id: string = "";
          protected abstract area(): number;
          override toString(): string { return ""; }
          declare accessor size: number;
        }
        interface HasArea { area(): number }
        enum Color { Red, Green }
        type Name<T> = T extends `get${infer R}` ? R : never;
        type Keys = keyof typeof obj;
        const config = { port: 80 } satisfies Config;
        function assertIsString(v: unknown): asserts v is string {}
        declare const sym: unique symbol;
        let n = value as number;
        let string = 1;
        """;

    private const string Jsx = """
        export function App({ items, ...rest }) {
          return (
            <Layout.Main title="Hi" data-id={1} {...rest}>
              {/* a comment */}
              <ul className='list'>
                {items.map((item) => <li key={item.id}>{item.name} &amp; more</li>)}
              </ul>
              <>
                <input disabled />
              </>
            </Layout.Main>
          );
        }
        const after = total / 2;
        """;

    [Test]
    public static void TheLatestJavaScriptHasItsColours()
    {
        var spans = Spans(ScriptDialect.JavaScript, LatestJavaScript);
        Expect(spans, "#!/usr/bin/env node", TokenClasses.PreprocessorText, "a hashbang (ES2023)");
        Expect(spans, "/// <reference types=\"node\" />", TokenClasses.PreprocessorText, "a triple-slash reference");
        Expect(spans, "with", TokenClasses.Keyword, "import attributes' with (ES2025)");
        Expect(spans, "defer", TokenClasses.Keyword, "import defer (stage 3, confirm)");
        Expect(spans, "source", TokenClasses.Keyword, "import source (stage 3, confirm)");
        Expect(spans, "@param", TokenClasses.DocCommentTag, "a JSDoc tag");
        Expect(spans, "* Adds two numbers.", TokenClasses.DocComment, "JSDoc's text (a continued line from its first character)");
        Expect(spans, "@logged", TokenClasses.PreprocessorKeyword, "a standard decorator");
        Expect(spans, "Counter", TokenClasses.ClassName, "a class's name");
        Expect(spans, "Base", TokenClasses.ClassName, "the class it extends");
        Expect(spans, "static", TokenClasses.Keyword, "static");
        Expect(spans, "#count", TokenClasses.FieldName, "a private name");
        Expect(spans, "#secret", TokenClasses.FieldName, "a private name after this.");
        Expect(spans, "0n", TokenClasses.Number, "a BigInt");
        Expect(spans, "accessor", TokenClasses.Keyword, "an auto-accessor");
        Expect(spans, "1_000_000", TokenClasses.Number, "numeric separators");
        Expect(spans, "0xFF_FF", TokenClasses.Number, "a hex number with a separator");
        Expect(spans, "get", TokenClasses.Keyword, "a getter's get");
        Expect(spans, "total", TokenClasses.MethodName, "a method's name");
        Expect(spans, "in", TokenClasses.Keyword, "#x in obj (ES2022)");
        Expect(spans, "async", TokenClasses.Keyword, "async");
        Expect(spans, "yield", TokenClasses.ControlKeyword, "yield");
        Expect(spans, "await", TokenClasses.ControlKeyword, "await of await using");
        Expect(spans, "using", TokenClasses.Keyword, "using and await using (ES2026, confirm)");
        Expect(spans, "(?<year>", TokenClasses.StringEscape, "a named group");
        Expect(spans, "\\d", TokenClasses.StringEscape, "a regex escape");
        Expect(spans, "(?i:", TokenClasses.StringEscape, "a modifier group (ES2025)");
        Expect(spans, "\\p{L}", TokenClasses.StringEscape, "a property escape inside a v-flag class");
        Expect(spans, "dv", TokenClasses.Keyword, "the d and v flags");
        Expect(spans, "/", TokenClasses.Operator, "a division");
        Expect(spans, "`", TokenClasses.String, "a template's backtick");
        Expect(spans, "${", TokenClasses.Punctuation, "a template's ${");
        Expect(spans, "\\n", TokenClasses.StringEscape, "an escape in a template");
        Expect(spans, "of", TokenClasses.Keyword, "for ... of");
        Expect(spans, "/x/", TokenClasses.String, "a regex after if (...)");
        Expect(spans, "test", TokenClasses.MethodName, "a method called");
        Expect(spans, "for", TokenClasses.ControlKeyword, "for");
        Check.True(!spans.Any(s => s.Text == "Counter" && s.Class == TokenClasses.Keyword), "names are not keywords");
    }

    [Test]
    public static void TheLatestTypeScriptHasItsColours()
    {
        var spans = Spans(ScriptDialect.TypeScript, LatestTypeScript);
        Expect(spans, "type", TokenClasses.Keyword, "import type and export type *");
        Expect(spans, "require", TokenClasses.MethodName, "import x = require()");
        Expect(spans, "declare", TokenClasses.Keyword, "declare");
        Expect(spans, "module", TokenClasses.Keyword, "declare module");
        Expect(spans, "global", TokenClasses.Keyword, "declare global");
        Expect(spans, "Window", TokenClasses.InterfaceName, "an interface's name");
        Expect(spans, "NS", TokenClasses.NamespaceName, "a namespace's name");
        Expect(spans, "abstract", TokenClasses.Keyword, "abstract");
        Expect(spans, "Shape", TokenClasses.ClassName, "a class's name");
        Expect(spans, "const", TokenClasses.Keyword, "a const type parameter");
        Expect(spans, "out", TokenClasses.Keyword, "in/out variance (TS 4.7)");
        Expect(spans, "implements", TokenClasses.Keyword, "implements");
        Expect(spans, "HasArea", TokenClasses.InterfaceName, "the interface implemented, and declared");
        Expect(spans, "private", TokenClasses.Keyword, "private");
        Expect(spans, "readonly", TokenClasses.Keyword, "readonly");
        Expect(spans, "string", TokenClasses.Keyword, "a predefined type after ':' before '='");
        Expect(spans, "number", TokenClasses.Keyword, "number");
        Expect(spans, "override", TokenClasses.Keyword, "override (TS 4.3)");
        Expect(spans, "toString", TokenClasses.MethodName, "a method's name");
        Expect(spans, "accessor", TokenClasses.Keyword, "declare accessor (TS 4.9)");
        Expect(spans, "interface", TokenClasses.Keyword, "interface");
        Expect(spans, "Color", TokenClasses.EnumName, "an enum's name");
        Expect(spans, "Name", TokenClasses.ClassName, "a type alias's name");
        Expect(spans, "infer", TokenClasses.Keyword, "infer in a template literal type");
        Expect(spans, "${", TokenClasses.Punctuation, "a template literal type's ${");
        Expect(spans, "never", TokenClasses.Keyword, "never");
        Expect(spans, "keyof", TokenClasses.Keyword, "keyof");
        Expect(spans, "typeof", TokenClasses.Keyword, "typeof");
        Expect(spans, "satisfies", TokenClasses.Keyword, "satisfies (TS 4.9)");
        Expect(spans, "asserts", TokenClasses.Keyword, "asserts");
        Expect(spans, "is", TokenClasses.Keyword, "a type predicate's is");
        Expect(spans, "unique", TokenClasses.Keyword, "unique symbol");
        Expect(spans, "symbol", TokenClasses.Keyword, "symbol");
        Expect(spans, "as", TokenClasses.Keyword, "as");
        int stringVariable = LatestTypeScript.IndexOf("let string = 1", StringComparison.Ordinal) + 4;
        Check.True(!SpansAt(ScriptDialect.TypeScript, LatestTypeScript).Any(s => s.Start == stringVariable && s.Class == TokenClasses.Keyword),
            "let string = 1: a variable named string is not the type");
        Check.True(Spans(ScriptDialect.JavaScript, "x satisfies y; let type = 1; declare(x);").All(s => s.Class != TokenClasses.Keyword || s.Text == "let"),
            "TypeScript's words are names in JavaScript");
    }

    [Test]
    public static void JsxAndTsxHaveTheirColoursAndPlainTypeScriptNeverLexesJsx()
    {
        var spans = Spans(ScriptDialect.Jsx, Jsx);
        Expect(spans, "Layout.Main", TokenClasses.ClassName, "a component (a dotted name)");
        Expect(spans, "ul", TokenClasses.Keyword, "an element's tag");
        Expect(spans, "li", TokenClasses.Keyword, "a nested element's tag");
        Expect(spans, "input", TokenClasses.Keyword, "a self-closing element");
        Expect(spans, "title", TokenClasses.PropertyName, "an attribute");
        Expect(spans, "data-id", TokenClasses.PropertyName, "a dashed attribute");
        Expect(spans, "disabled", TokenClasses.PropertyName, "an attribute without a value");
        Expect(spans, "\"Hi\"", TokenClasses.String, "an attribute's string");
        Expect(spans, "'list'", TokenClasses.String, "an attribute's single-quoted string");
        Expect(spans, "/* a comment */", TokenClasses.Comment, "a comment in children's braces");
        Expect(spans, "&amp;", TokenClasses.StringEscape, "an entity in text");
        Expect(spans, "map", TokenClasses.MethodName, "code inside an attribute's braces");
        Expect(spans, "</", TokenClasses.Punctuation, "a closing tag's </");
        Expect(spans, "/>", TokenClasses.Punctuation, "a self-closing />");
        Expect(spans, "/", TokenClasses.Operator, "after the JSX, a division: the elements closed across lines");
        Check.True(!spans.Any(s => s.Text == "more"), "children's text is plain");

        var tsx = Spans(ScriptDialect.Tsx, "const id = <T,>(x: T) => x;\nconst f = <T extends object>(x: T) => x;\nconst g = <const T,>(x: T) => x;\nconst el = <div className=\"a\">{x as number}</div>;\nconst h = 1 / 2;");
        Check.True(tsx.Count(s => s.Text == "<" && s.Class == TokenClasses.Operator) == 3, "<T,>, <T extends U> and <const T,> are generics in .tsx, not elements");
        Expect(tsx, "extends", TokenClasses.Keyword, "a constraint");
        Expect(tsx, "div", TokenClasses.Keyword, "an element in .tsx");
        Expect(tsx, "as", TokenClasses.Keyword, "TypeScript inside JSX braces");
        Expect(tsx, "/", TokenClasses.Operator, "a division after the element");

        var ts = Spans(ScriptDialect.TypeScript, "const y = <string>value;\nconst z = a < b;");
        Check.True(!ts.Any(s => s.Class == TokenClasses.PropertyName || (s.Text == "string" && s.Class == TokenClasses.ClassName)), "plain .ts never lexes JSX: <string>value is a type assertion");
        Expect(ts, "string", TokenClasses.Keyword, "the asserted type");
        Check.True(ts.Count(s => s.Text == "<" && s.Class == TokenClasses.Operator) == 2, "both '<' are operators in .ts");
        var js = Spans(ScriptDialect.JavaScript, "const a = <div>x</div>;");
        Check.True(!js.Any(s => s.Text == "div" && s.Class == TokenClasses.Keyword), "plain JavaScript does not lex JSX");
    }

    [Test]
    public static void RegexOrDivisionIsDecidedByTheTokenBefore()
    {
        (string Code, bool Regex)[] table =
        [
            ("a = b / c", false),
            ("a = /re/", true),
            ("x = (a) / 2", false),
            ("if (x) /re/.test(y)", true),
            ("while (x) /a/g.exec(s)", true),
            ("for (;;) /a/.test(s)", true),
            ("with (o) /a/.test(s)", true),
            ("{ }\n/re/.test(x)", true),
            ("x = { a: 1 } / 2", false),
            ("x = {}\n/ 2", false),
            ("return /re/", true),
            ("typeof /re/", true),
            ("case /re/:", true),
            ("yield /re/", true),
            ("await /re/", true),
            ("a[0] / 2", false),
            ("a++ / 2", false),
            ("++/re/.lastIndex", true),
            ("x.return / 2", false),
            ("this / 2", false),
            ("1 / 2", false),
            ("\"s\" / 2", false),
            ("`t` / 2", false),
            ("(x) => /re/", true),
            ("a ? /re/ : b", true),
            ("[/re/]", true),
            ("f(/re/)", true),
            ("a, /re/", true),
            ("x = y\n/ 2 / 3", false),
            ("let re = /[/]/", true),
            ("a /= 2", false),
            ("import x from \"y\"; /re/", true),
            ("else /re/", true),
            ("const r = /a(?<n>b)\\k<n>/", true),
            ("f() / 2", false),
            ("x!", false),
        ];
        var problems = new List<string>();
        foreach (var (code, regex) in table)
        {
            int slash = code.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0)
            {
                continue;
            }

            var spans = SpansAt(ScriptDialect.TypeScript, code);
            var at = spans.FirstOrDefault(s => s.Start == slash);
            bool isRegex = at.Class == TokenClasses.String;
            bool isDivision = at.Class == TokenClasses.Operator;
            if (regex ? !isRegex : !isDivision)
            {
                problems.Add($"{code.Replace("\n", "\\n", StringComparison.Ordinal)}: expected {(regex ? "a regex" : "a division")}, got {at.Class ?? "no span"}");
            }
        }

        Check.True(problems.Count == 0, "regex or division:\n  " + string.Join("\n  ", problems));
        Expect(Spans(ScriptDialect.JavaScript, "let re = /[/]/g;"), "/[/]/", TokenClasses.String, "a '/' inside a class does not end the regex");
    }

    [Test]
    public static void TemplatesNestAcrossLinesToAnyDepth()
    {
        const string text = """
            const html = `
              <ul>
                ${items.map(item => `
                  <li>${item.name.replace(/</g, "&lt;")} ${`deep ${`deeper ${x}`}`}</li>
                `).join("")}
              </ul>
            `;
            const after = 1 / 2;
            """;
        var states = new LineStates();
        var tokenizer = new ScriptTokenizer(ScriptDialect.Jsx, ScriptVersion.Newest, states);
        var spans = Spans(tokenizer, text);
        Expect(spans, "/</", TokenClasses.String, "a regex inside ${ } inside a template spanning lines");
        Expect(spans, "join", TokenClasses.MethodName, "code after the inner template closed on a later line");
        Check.True(!spans.Any(s => s.Text is "ul" or "li" && s.Class == TokenClasses.Keyword), "markup inside a template is its text, not JSX");
        Expect(spans, "const", TokenClasses.Keyword, "after the template, code again");
        var last = spans.Last(s => s.Text == "/");
        Check.Equal(TokenClasses.Operator, last.Class, "and the state is back to code (a division)");
        Check.True(states.Count >= 2, $"the stacks of frames across lines are interned ({states.Count})");

        // 40 levels deep across lines, then closed: back to code; past the table's depth, the fallback (counted).
        var deep = new StringBuilder("const d = ");
        for (int i = 0; i < 40; i++)
        {
            deep.Append("`a${\n");
        }

        deep.Append('x');
        for (int i = 0; i < 40; i++)
        {
            deep.Append("}b`\n");
        }

        deep.Append("; const e = 1 / 2;");
        var deepSpans = Spans(tokenizer, deep.ToString());
        Check.Equal(TokenClasses.Operator, deepSpans.Last(s => s.Text == "/").Class, "40 nested templates across lines close back to code");
        var shallow = new LineStates(capacity: 64, maxDepth: 8);
        var bounded = new ScriptTokenizer(ScriptDialect.JavaScript, ScriptVersion.Newest, shallow);
        Spans(bounded, deep.ToString());
        Check.True(shallow.Overflows > 0, $"deeper than the table's 8 frames: the fallback state, counted ({shallow.Overflows})");
    }

    [Test]
    public static void RandomEditsEqualAFreshSession()
    {
        string[] fragments = ["`", "`", "${", "${", "}", "}", "/", "/", "*", "<", "<", ">", "\"", "'", "\n", "\n", "\n", "/*", "*/", "//", "x", " ", "(", ")", "{", "<div>", "</div>", "/re/g", "a / b"];
        var problems = 0;
        foreach (var (dialect, seed, fixture) in new[] { (ScriptDialect.Jsx, 7, Jsx + LatestJavaScript), (ScriptDialect.Tsx, 11, LatestTypeScript + Jsx), (ScriptDialect.TypeScript, 13, LatestTypeScript) })
        {
            var tokenizer = new ScriptTokenizer(dialect, ScriptVersion.Newest, new LineStates(capacity: 1 << 16));
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
                        Check.SequenceEqual(expected.Spans.ToArray(), into.Spans.ToArray(), $"{dialect}, round {round}: the spans equal a fresh session's");
                    }
                }
            }
        }

        Check.Equal(0, problems, "4,500 random edits (weighted to ` ${ } / * < > quotes and line breaks) equal a fresh session");
    }

    [Test]
    public static void AWarmScreenAllocatesNothingAndAKeystrokeReadsOneLine()
    {
        var tokenizer = new ScriptTokenizer(ScriptDialect.Tsx, ScriptVersion.Newest, new LineStates());
        var text = Repeat(LatestTypeScript + "\n" + Jsx + "\nconst t = `a ${b} c\n d ${`e`} f`;\n", 2000);
        var buffer = new PieceTableBuffer(text);
        using var session = new LineStateSession(buffer, tokenizer, null, LanguageSettings.Default);
        using var into = new TokenSpanBuffer();
        var last = buffer.Current.GetLine(buffer.Current.LineCount - 1);
        session.Classify(buffer.Current, last.Start, last.Length, into);
        var screen = buffer.Current.GetLine(1000);
        var screenEnd = buffer.Current.GetLine(1059);
        for (int warm = 0; warm < 50; warm++)
        {
            into.Clear();
            session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 200; frame++)
        {
            into.Clear();
            session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.Equal(0L, allocated, "200 warm screens of 60 lines (templates and JSX interned) allocate nothing");

        // Typing a letter in a name re-reads that line only: the line below ends as it did, so the rest settles.
        int lines = buffer.Current.LineCount;
        long scanned = session.LinesScanned;
        int at = -1;
        for (int index = 1000; index < 1060 && at < 0; index++)
        {
            var target = buffer.Current.GetLine(index);
            int name = buffer.Current.GetText(target.Start, target.Length).IndexOf("Shape<", StringComparison.Ordinal);
            at = name < 0 ? -1 : target.Start + name + 2;
        }

        Check.True(at >= 0, "a class's name is on the screen");
        buffer.Apply([TextEdit.Insert(at, "x")]);
        into.Clear();
        session.Classify(buffer.Current, screen.Start, screenEnd.End - screen.Start, into);
        long perKeystroke = session.LinesScanned - scanned;
        Check.True(perKeystroke <= 1 && session.KnownLines >= lines - 1, $"a keystroke re-reads at most its line ({perKeystroke}) and the states below settle ({session.KnownLines} of {lines} known)");
    }

    [Test]
    public static void TheWorstFlipReadsOnlyToTheScreensEndAndTheNumbersAreWritten()
    {
        // A 100,000-line TypeScript file: a backtick typed on line 1 turns everything below into a template's text.
        var tokenizer = new ScriptTokenizer(ScriptDialect.TypeScript, ScriptVersion.Newest, new LineStates());
        var line = "export const value: number = compute(a, b) / 2; // a comment\n";
        var text = new StringBuilder(line.Length * 100_000);
        text.Append("// header\n");
        for (int i = 1; i < 100_000; i++)
        {
            text.Append(line);
        }

        var buffer = new PieceTableBuffer(text.ToString());
        using var session = new LineStateSession(buffer, tokenizer, null, LanguageSettings.Default);
        using var into = new TokenSpanBuffer();
        var screenEnd = buffer.Current.GetLine(59);
        long start = Stopwatch.GetTimestamp();
        session.Classify(buffer.Current, 0, screenEnd.End, into);
        double coldScreen = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        var bottom = buffer.Current.GetLine(buffer.Current.LineCount - 1);
        into.Clear();
        session.Classify(buffer.Current, bottom.Start, bottom.Length, into);
        double fullScan = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Check.True(session.KnownLines >= 99_999, "the whole file's states are known");

        for (int warm = 0; warm < 50; warm++)
        {
            into.Clear();
            session.Classify(buffer.Current, 0, screenEnd.End, into);
        }

        const int Frames = 1000;
        start = Stopwatch.GetTimestamp();
        for (int frame = 0; frame < Frames; frame++)
        {
            into.Clear();
            session.Classify(buffer.Current, 0, screenEnd.End, into);
        }

        double warmScreen = Stopwatch.GetElapsedTime(start).TotalMilliseconds / Frames;

        long scanned = session.LinesScanned;
        var line1 = buffer.Current.GetLine(1);
        start = Stopwatch.GetTimestamp();
        buffer.Apply([TextEdit.Insert(line1.Start, "`")]);
        into.Clear();
        screenEnd = buffer.Current.GetLine(59);
        session.Classify(buffer.Current, 0, screenEnd.End, into);
        double flip = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long flipLines = session.LinesScanned - scanned;
        Check.True(flipLines <= 60 && session.KnownLines <= 61, $"the flip re-reads only to the screen's end ({flipLines} lines read, {session.KnownLines} known), not the 100,000 below");
        Check.True(into.Spans.ToArray().Any(s => s.Class == TokenClasses.String && buffer.Current.GetLineIndex(s.Start) == 30), "the lines below the backtick are the template's text now");

        // The formatter and the folding on a 2,000-line file, and the per-keystroke count, for the bench file.
        var fileText = Repeat(LatestTypeScript + "\n" + Jsx + "\n", 2000);
        var formatter = new ScriptFormatter(ScriptTokenizer.For(ScriptDialect.Tsx, ScriptVersion.Newest));
        var edits = new List<TextEdit>();
        start = Stopwatch.GetTimestamp();
        formatter.Format(fileText, new FormatStyle("  ", "\n"), edits);
        double formatCold = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        edits.Clear();
        start = Stopwatch.GetTimestamp();
        formatter.Format(fileText, new FormatStyle("  ", "\n"), edits);
        double formatWarm = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var snapshot = new PieceTableBuffer(fileText).Current;
        start = Stopwatch.GetTimestamp();
        var ranges = ScriptFolding.Ranges(ScriptTokenizer.For(ScriptDialect.Tsx, ScriptVersion.Newest), snapshot);
        double folding = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Check.True(ranges.Count > 100, $"the file folds ({ranges.Count} ranges)");

        string report = string.Create(CultureInfo.InvariantCulture, $"""
            W-script bench (test WebScript.TheWorstFlipReadsOnlyToTheScreensEndAndTheNumbersAreWritten), {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC, {Environment.ProcessorCount} cores, .NET {Environment.Version}
            100,000-line TypeScript file ({buffer.Current.Length / 1024} KiB):
              first screen of 60 lines, cold (JIT included):     {coldScreen,8:F3} ms
              the rest of the file's states, first read:         {fullScan,8:F1} ms
              warm screen of 60 lines (mean of {Frames}):           {warmScreen,8:F4} ms, 0 bytes (AWarmScreenAllocatesNothing...)
              a backtick typed on line 1, then the screen:       {flip,8:F3} ms, {flipLines} lines read (to the screen's end, not the 100,000 below)
              a letter typed in a name:                          1 line read, the rest settles
            2,000-line TSX file ({fileText.Length / 1024} KiB):
              Format document, cold:                             {formatCold,8:F1} ms
              Format document, warm:                             {formatWarm,8:F1} ms ({edits.Count} edits)
              folding ranges:                                    {folding,8:F1} ms ({ranges.Count} ranges)

            """);
        string folder = TestRunner.RepositoryPath("test-output", "benches");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "web-script.txt"), report);
        Console.WriteLine(report);
    }

    [Test]
    public static void TheVersionGateReadsNewerWordsAsNames()
    {
        static bool IsKeyword(ScriptDialect dialect, ScriptVersion version, string code, string word) =>
            Spans(new ScriptTokenizer(dialect, version), code).Any(s => s.Text == word && s.Class == TokenClasses.Keyword);

        Check.True(!IsKeyword(ScriptDialect.TypeScript, new(ScriptVersion.Latest, 408), "x satisfies T;", "satisfies"), "satisfies is a name before TypeScript 4.9");
        Check.True(IsKeyword(ScriptDialect.TypeScript, new(ScriptVersion.Latest, 409), "x satisfies T;", "satisfies"), "and a keyword from 4.9");
        Check.True(!IsKeyword(ScriptDialect.TypeScript, new(ScriptVersion.Latest, 501), "using x = y;", "using"), "using is a name before TypeScript 5.2");
        Check.True(IsKeyword(ScriptDialect.TypeScript, new(ScriptVersion.Latest, 502), "using x = y;", "using"), "and a keyword from 5.2");
        Check.True(!IsKeyword(ScriptDialect.TypeScript, new(ScriptVersion.Latest, 402), "class A { override m() {} }", "override"), "override before 4.3");
        Check.True(!IsKeyword(ScriptDialect.JavaScript, new(2025, ScriptVersion.Latest), "using x = y;", "using"), "using is a name in ES2025");
        Check.True(IsKeyword(ScriptDialect.JavaScript, new(2026, ScriptVersion.Latest), "using x = y;", "using"), "and a keyword in ES2026");
        Check.True(!IsKeyword(ScriptDialect.JavaScript, new(2026, ScriptVersion.Latest), "class A { accessor x = 1; }", "accessor"), "accessor waits for ESNext (decorators, stage 3)");
        Check.True(IsKeyword(ScriptDialect.JavaScript, ScriptVersion.Newest, "class A { accessor x = 1; }", "accessor"), "and is read at the latest");
        Check.True(!IsKeyword(ScriptDialect.JavaScript, ScriptVersion.Newest, "using(x);", "using"), "using(x) is a call");

        Check.Equal(2022, ScriptVersion.ParseEcmaScript("ES2022"), "ES2022");
        Check.Equal(2015, ScriptVersion.ParseEcmaScript("es6"), "ES6 is ES2015");
        Check.Equal(2009, ScriptVersion.ParseEcmaScript("ES5"), "ES5");
        Check.Equal(ScriptVersion.Latest, ScriptVersion.ParseEcmaScript("ESNext"), "ESNext");
        Check.Equal(null, ScriptVersion.ParseEcmaScript("node"), "not a version");
        Check.Equal(504, ScriptVersion.ParseTypeScript("^5.4.2"), "a caret range");
        Check.Equal(409, ScriptVersion.ParseTypeScript("~4.9"), "a tilde range");
        Check.Equal(500, ScriptVersion.ParseTypeScript(">=5.0.0 <6"), "a range's first version");
        Check.Equal(ScriptVersion.Latest, ScriptVersion.ParseTypeScript("latest"), "latest");
        Check.Equal(null, ScriptVersion.ParseTypeScript("workspace"), "not a version");
    }

    [Test]
    public static async Task TheProjectsVersionIsReadOnceAndASessionSwitchesThroughReclassified()
    {
        using var temp = new TempFolder();
        string root = temp.Path;
        Directory.CreateDirectory(Path.Combine(root, "src"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules", "typescript"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules", "@tsconfig", "node18"));
        await File.WriteAllTextAsync(Path.Combine(root, "tsconfig.json"), "{\n  // the app's\n  \"extends\": \"./tsconfig.base.json\",\n  \"compilerOptions\": { \"strict\": true, },\n}");
        await File.WriteAllTextAsync(Path.Combine(root, "tsconfig.base.json"), "{ \"extends\": \"@tsconfig/node18/tsconfig.json\" }");
        await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "@tsconfig", "node18", "tsconfig.json"), "{ \"compilerOptions\": { \"target\": \"es2022\" } }");
        await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "typescript", "package.json"), "﻿{ \"name\": \"typescript\", \"version\": \"4.8.4\" }");
        await File.WriteAllTextAsync(Path.Combine(root, "package.json"), "{ \"devDependencies\": { \"typescript\": \"^5.6.0\" }, \"dependencies\": { \"react\": \"^19.0.0\" } }");
        ScriptProjects.Forget();

        var typeScript = new ScriptLanguage("typescript-test", "a test's TypeScript", [".ts"], typeScript: true);
        string file = Path.Combine(root, "src", "app.ts");
        var buffer = new PieceTableBuffer("const c = x satisfies T;\n");
        using var session = (LineStateSession)typeScript.Open(buffer, new LanguageOpenContext(file, root, LanguageSettings.Default));
        Check.True(session.Tokenizer is ScriptTokenizer { Version.TypeScript: ScriptVersion.Latest }, "a file whose project is not read yet opens at once at the latest");
        int reclassified = 0;
        session.Reclassified += (_, _) => reclassified++;
        await typeScript.LastRead!;
        Check.True(session.Tokenizer is ScriptTokenizer { Version.TypeScript: 408, Dialect: ScriptDialect.TypeScript }, $"then the installed TypeScript (node_modules/typescript: 4.8.4) is read ({(session.Tokenizer as ScriptTokenizer)?.Version})");
        Check.Equal(1, reclassified, "the session changed dialect through Reclassified");
        using var spans = new TokenSpanBuffer();
        session.Classify(buffer.Current, 0, buffer.Current.Length, spans);
        Check.True(!spans.Spans.ToArray().Any(s => s.Class == TokenClasses.Keyword && buffer.Current.GetText(s.Start, s.Length) == "satisfies"), "satisfies reads as a name in 4.8");
        Check.Equal(1L, ScriptProjects.Reads, "the project's folder was read once");
        var project = ScriptProjects.Find(file)!;
        Check.True(project is { EcmaScript: 2022, TypeScript: 408, UsesJsx: true }, $"its target through two extends (a path, then a package), its TypeScript, React ({project})");

        using var second = (LineStateSession)typeScript.Open(new PieceTableBuffer("let a;"), new LanguageOpenContext(Path.Combine(root, "src", "other.ts"), root, LanguageSettings.Default));
        Check.True(second.Tokenizer is ScriptTokenizer { Version.TypeScript: 408 } && ScriptProjects.Reads == 1, "a second file of the folder opens in the project's version at once, nothing read again");

        // The setting first, then the project, then the latest.
        Check.Equal(500, typeScript.VersionFor(project, new ScriptSettings { Version = "5.0" }).TypeScript, "the setting's version wins");
        Check.Equal(ScriptVersion.Latest, typeScript.VersionFor(project, new ScriptSettings { ProjectVersion = false }).TypeScript, "projectVersion off: the latest");
        Check.Equal(408, typeScript.VersionFor(project, new ScriptSettings()).TypeScript, "else the project's");

        // JavaScript: the target from tsconfig (no jsconfig), React's JSX in .js by auto, on and off.
        var javaScript = new ScriptLanguage("javascript-test", "a test's JavaScript", [".js", ".jsx"], typeScript: false);
        Check.Equal(2022, javaScript.VersionFor(project, new ScriptSettings()).EcmaScript, "JavaScript reads the target");
        Check.Equal(ScriptDialect.Jsx, javaScript.DialectFor("a.js", project, new ScriptSettings()), "auto: a React project's .js reads JSX");
        Check.Equal(ScriptDialect.JavaScript, javaScript.DialectFor("a.js", null, new ScriptSettings()), "auto, no React: plain JavaScript");
        Check.Equal(ScriptDialect.JavaScript, javaScript.DialectFor("a.js", project, new ScriptSettings { Jsx = "off" }), "off");
        Check.Equal(ScriptDialect.Jsx, javaScript.DialectFor("a.js", null, new ScriptSettings { Jsx = "on" }), "on");
        Check.Equal(ScriptDialect.Jsx, javaScript.DialectFor("a.jsx", null, new ScriptSettings { Jsx = "off" }), ".jsx always");
        Check.Equal(ScriptDialect.Tsx, typeScript.DialectFor("a.tsx", null, new ScriptSettings()), ".tsx");
        Check.Equal(ScriptDialect.TypeScript, typeScript.DialectFor("a.d.ts", null, new ScriptSettings()), ".d.ts");
        using var js = (LineStateSession)javaScript.Open(new PieceTableBuffer("using x = y;"), new LanguageOpenContext(Path.Combine(root, "src", "a.js"), root, LanguageSettings.Default));
        Check.True(js.Tokenizer is ScriptTokenizer { Dialect: ScriptDialect.Jsx, Version.EcmaScript: 2022 }, "a .js of the read project opens with JSX and ES2022 at once");

        var settings = ScriptSettings.Read(key => key switch { "version" => " es2020 ", "jsx" => "ON", "formatLineWidth" => "80", "stateDepth" => "500", _ => null });
        Check.True(settings is { Version: "es2020", Jsx: "on", FormatLineWidth: 80, StateDepth: LineStates.DefaultMaxDepth, ProjectVersion: true }, $"settings parse, a bad value keeps its default ({settings})");
        ScriptProjects.Forget();
    }

    [Test]
    public static void TheFormatterChangesOnlyWhiteSpaceAndIsIdempotent()
    {
        var formatter = new ScriptFormatter(ScriptTokenizer.For(ScriptDialect.Tsx, ScriptVersion.Newest));
        var style = new FormatStyle("    ", "\n");
        const string messy = "import {a,b} from 'x'\nfunction  f(x,y){\nif(x>y){return x+y}\nelse{\nreturn -x\n}\n}\nconst o={a:1,b:[1,2,3]}\nfor(let i=0;i<10;i++){console.log(i)}\nconst s=`keep   ${ a+b }   this`   \n\n\n\nlet z = a ?b:c;\n";
        const string formatted = "import { a, b } from 'x'\nfunction f(x, y) {\n    if (x>y) { return x + y }\n    else {\n        return -x\n    }\n}\nconst o = { a: 1, b: [1, 2, 3] }\nfor (let i = 0; i<10; i++) { console.log(i) }\nconst s = `keep   ${a + b}   this`\n\nlet z = a ?b: c;\n";
        Check.Equal(formatted, Format(formatter, messy, style), "formatted: one space after keywords and around binary operators, braces spaced, a template's text kept, one empty line kept");
        Check.Equal(formatted, Format(formatter, formatted, style), "formatting again changes nothing");

        const string structure = "const total = items\n.filter(x => x.ok)\n.map(x => x.n);\nif (ready)\ngo();\nconst view = (\n<div className=\"a\"\nid={b}>\n<span>{name}</span>\n</div>\n);\nclass A {\n/**\n* Doc.\n*/\nm() {}\n}\ndescribe('x', () => {\nit('y', async () => {\nawait run();\n});\n});\nconst t = `\n  kept as written\n`;\n";
        const string structured = "const total = items\n    .filter(x => x.ok)\n    .map(x => x.n);\nif (ready)\n    go();\nconst view = (\n    <div className=\"a\"\n        id={b}>\n        <span>{name}</span>\n    </div>\n);\nclass A {\n    /**\n     * Doc.\n     */\n    m() {}\n}\ndescribe('x', () => {\n    it('y', async () => {\n        await run();\n    });\n});\nconst t = `\n  kept as written\n`;\n";
        Check.Equal(structured, Format(formatter, structure, style), "indented by brackets (several on a line indent once), JSX nesting, continued expressions, JSDoc's lines; a template's lines kept");
        Check.Equal(structured, Format(formatter, structured, style), "and idempotent");
        Check.Equal(structured.Replace("    ", "\t", StringComparison.Ordinal), Format(formatter, structure, new FormatStyle("\t", "\n")), "tabs when the editor indents with tabs");
        Check.Equal("a = 1;\r\nif (a) {\r\n  b();\r\n}\r\n", Format(formatter, "a=1;\r\nif(a){\r\nb();\r\n}\r\n", new FormatStyle("  ", "\r\n")), "the tab size and the document's line break");

        foreach (var text in new[] { messy, structure, LatestJavaScript, LatestTypeScript, Jsx })
        {
            string once = Format(formatter, text, style);
            Check.Equal(Strip(text), Strip(once), "only white space changed");
            Check.Equal(once, Format(formatter, once, style), "idempotent on every fixture");
        }

        foreach (var (bad, where, what) in new[]
        {
            ("function f() {\n  return 1;\n", "line 1, column 14", "is not closed"),
            ("const a = (1, 2];", "line 1, column 16", "']' where ')' was expected"),
            ("const t = `abc\n", "line 1, column 11", "template literal is not closed"),
            ("const e = <div>x</span>;", "line 1, column 19", "</span> does not close <div>"),
            ("const s = \"abc;\n", "line 1, column 11", "string is not closed"),
            ("/* never closed\nconst a = 1;", "line 1, column 1", "comment is not closed"),
            ("}", "line 1, column 1", "closes nothing"),
        })
        {
            var error = Check.Throws<FormatException>(() => Format(formatter, bad, style), $"malformed is refused: {bad.Replace("\n", "\\n", StringComparison.Ordinal)}");
            Check.True(error.Message.StartsWith(where, StringComparison.Ordinal) && error.Message.Contains(what, StringComparison.Ordinal), $"the message says where and what ({error.Message})");
        }
    }

    [Test]
    public static void TheFormattersLineWidthBreaksAfterCommasAndStaysIdempotent()
    {
        var formatter = new ScriptFormatter(ScriptTokenizer.For(ScriptDialect.TypeScript, ScriptVersion.Newest), () => 32);
        var style = new FormatStyle("  ", "\n");
        const string text = "function f() {\n  call(alpha, beta, gamma, delta, epsilon, zeta, eta);\n  const short = [1, 2];\n}\n";
        string once = Format(formatter, text, style);
        Check.True(once.Split('\n').All(l => l.Length <= 32), $"every line fits 32 columns:\n{once}");
        Check.True(once.Contains("const short = [1, 2];", StringComparison.Ordinal), "a line that fits is left whole");
        Check.Equal(Strip(text), Strip(once), "only white space changed");
        Check.Equal(once, Format(formatter, once, style), "and formatting again changes nothing");
        Check.Equal(text, Format(new ScriptFormatter(ScriptTokenizer.For(ScriptDialect.TypeScript, ScriptVersion.Newest)), text, style), "with no width set, no line is broken");
    }

    [Test]
    public static async Task FormatKeepsTheCaretIsOneUndoStepAndFormatsASelection()
    {
        var language = new ScriptLanguage("javascript-format-test", "a test's JavaScript", [".js"], typeScript: false);
        var options = new CraftOptions { TabSize = 2, InsertSpaces = true };
        var (model, document, _) = Editors.MakeMarked("function f(){return x+|y}\n", options, language);
        model.Dispatch = action => action();
        int steps = document.Kinds.Count;
        Check.True(model.FormattingSession is LineStateSession, "JavaScript formats through the public kit's session");
        Check.True(await model.FormatDocumentAsync(), "the format applied edits");
        Check.Equal("function f() { return x + y }\n", document.Text, "formatted");
        Check.Equal(document.Text.IndexOf("y }", StringComparison.Ordinal), model.Selection.Caret, "the caret kept its place before y");
        Check.Equal(steps + 1, document.Kinds.Count, "as one step");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("function f(){return x+y}\n", document.Text, "one undo takes it back");

        var (broken, brokenDocument, _) = Editors.Make("function f() {\n  return `x\n", options, language);
        broken.Dispatch = action => action();
        Check.True(!await broken.FormatDocumentAsync() && brokenDocument.Text == "function f() {\n  return `x\n", "a malformed file is left unchanged");
        Check.True(broken.LastFormatMessage is { } message && message.Contains("line 2, column 10", StringComparison.Ordinal), $"with the reason ({broken.LastFormatMessage})");

        // Format Selection: only the edits inside the range.
        const string text = "a=1;\nb=2;\nc=3;\n";
        var buffer = new PieceTableBuffer(text);
        using var session = (LineStateSession)language.Open(buffer);
        var inside = await session.FormatAsync(buffer.Current, 5, 4, CancellationToken.None);
        Check.True(inside.Count > 0 && inside.All(e => e.Start >= 5 && e.End <= 9), "Format Selection edits only the selected line");
        buffer.Apply([.. inside]);
        Check.Equal("a=1;\nb = 2;\nc=3;\n", buffer.Current.GetText(0, buffer.Current.Length), "the lines around it are left as they were");
        model.Dispose();
        broken.Dispose();
    }

    [Test]
    public static void FoldingCoversBracesRegionsCommentsImportsJsxAndTemplates()
    {
        const string text = "import a from \"a\";\nimport {\n  b,\n} from \"b\";\nimport c from \"c\";\n\n// one\n// two\n//#region Helpers\nfunction f() {\n  return [\n    1,\n  ];\n}\n//#endregion\n/*\n block\n*/\nconst el = (\n  <div>\n    x\n  </div>\n);\nconst t = `\n  multi\n`;\n/* #region more */\nlet q = 1;\n/* #endregion */\n";
        var ranges = ScriptFolding.Ranges(ScriptTokenizer.For(ScriptDialect.Tsx, ScriptVersion.Newest), new PieceTableBuffer(text).Current);
        FoldingRange[] expected =
        [
            new(0, 4, FoldingRangeKind.Imports),
            new(1, 3),
            new(6, 7, FoldingRangeKind.Comment),
            new(8, 14, FoldingRangeKind.Marked),
            new(9, 13),
            new(10, 12),
            new(15, 17, FoldingRangeKind.Comment),
            new(18, 22),
            new(19, 21),
            new(23, 25),
            new(26, 28, FoldingRangeKind.Marked),
        ];
        var sorted = ranges.OrderBy(r => r.StartLine).ThenByDescending(r => r.EndLine).ToArray();
        Check.SequenceEqual(expected, sorted, "imports, the import's braces, a comment run, a region, braces, brackets, a block comment, parentheses, a JSX element, a template, a /* #region */");
    }

    [Test]
    public static void InsideJsxTheRulesAreJsxsAndTheAngularStubReadsPipes()
    {
        var tokenizer = ScriptTokenizer.For(ScriptDialect.Jsx, ScriptVersion.Newest);
        const string text = "const a = (\n  <div>\n    text\n  </div>\n);\n";
        var buffer = new PieceTableBuffer(text);
        using var session = new LineStateSession(buffer, tokenizer, null, LanguageSettings.Default);
        var inChildren = session.RulesAt(buffer.Current, text.IndexOf("text", StringComparison.Ordinal));
        Check.True(inChildren is { LineComment: null, BlockCommentOpen: "{/*", BlockCommentClose: "*/}" }, "between tags, a comment is {/* */}");
        Check.True(session.RulesAt(buffer.Current, 0) is null, "in code, the language's own rules (//)");
        Check.True(ScriptLanguage.ScriptRules is { LineComment: "//", BlockCommentOpen: "/*", StringDelimiters: "\"'`" }, "JavaScript's rules");

        var angular = Spans(ScriptDialect.AngularExpression, "items | slice:0:3 | json; a / b / c");
        Expect(angular, "slice", TokenClasses.MethodName, "a pipe's name");
        Expect(angular, "json", TokenClasses.MethodName, "another pipe");
        Check.True(angular.Count(s => s.Text == "/" && s.Class == TokenClasses.Operator) == 2, "no regular expression in an Angular expression");
        Check.True(Spans(ScriptDialect.AngularExpression, "x < y").All(s => s.Class != TokenClasses.Keyword || s.Text != "y"), "and no JSX");
    }

    // ---- helpers ----

    private static List<(string Text, string Class)> Spans(ScriptDialect dialect, string text) => Spans(new ScriptTokenizer(dialect, ScriptVersion.Newest), text);

    private static List<(string Text, string Class)> Spans(ScriptTokenizer tokenizer, string text) =>
        [.. SpansAt(tokenizer, text).Select(s => (text.Substring(s.Start, s.Length), s.Class))];

    private static List<TokenSpan> SpansAt(ScriptDialect dialect, string text) => SpansAt(new ScriptTokenizer(dialect, ScriptVersion.Newest), text);

    private static List<TokenSpan> SpansAt(ScriptTokenizer tokenizer, string text)
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

    private static string Strip(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    private static string Repeat(string text, int lines)
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
