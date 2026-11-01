// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;
using Craft.Editor;
using Craft.Plugins.Css;
using Craft.Plugins.Html;
using Craft.Tests.Editor;
using Craft.Tests.Runner;
using Craft.Web.Markup;
using Craft.Web.Style;

namespace Craft.Tests;

/// <summary>
/// The owner's pick 3a: built-in HTML and CSS completion, no Node and no language server. Elements, attributes (global, the
/// element's own, ARIA, data-, events), fixed values, end tags (after &lt;/ and after a start tag's &gt;), entities; the CSS of
/// &lt;style&gt; and style="…"; CSS, SCSS and LESS properties, values per property (functions, colours, var(--x) from the
/// file), at-rules, pseudo-classes and pseudo-elements, SCSS's variables and mixins; Angular's blocks as snippets and its
/// bindings; through the editor (the list opens on the language's trigger characters, '-' filters, a commit is one edit),
/// the settings turning it off, ghost text still beside it; and the time to the list, cold and warm
/// (test-output/benches/web-completion.txt).
/// </summary>
[TestGroup("WebCompletion")]
public static class WebCompletionTests
{
    [Test]
    public static async Task ElementsAttributesAndValuesComplete()
    {
        var html = HtmlTokenizer.For(HtmlOptions.Html);
        var tags = await Complete(html, "<p>x</p>\n<|", '<');
        Check.True(Has(tags, "div") && Has(tags, "section") && Has(tags, "dialog") && Has(tags, "search"), $"'<' offers the living standard's elements ({Labels(tags)})");
        Check.Equal("<p>x</p>\n<div|", Apply("<p>x</p>\n<|", tags!, "div"), "an element committed");
        Check.Equal("<div|", Apply("<di|", (await Complete(html, "<di|", CompletionTrigger.Invoke))!, "div"), "the typed part is replaced");

        var attributes = await Complete(html, "<input |", ' ');
        Check.True(Detail(attributes, "type") == "attribute of <input>" && Has(attributes, "class") && Has(attributes, "aria-label") && Has(attributes, "onclick") && Has(attributes, "data-"),
            $"in a start tag: the element's own, the globals, ARIA, events and data- ({Labels(attributes)})");
        Check.Equal("<input type=\"|\"", Apply("<input |", attributes!, "type"), "an attribute with a value: the caret between its quotes");
        Check.Equal("<input disabled|", Apply("<input |", attributes!, "disabled"), "a boolean attribute: its name alone");
        Check.Equal("<input data-|", Apply("<input |", attributes!, "data-"), "data- for its name to go on");
        var multi = await Complete(html, "<input\n  ty|", CompletionTrigger.Invoke);
        Check.Equal("attribute of <input>", Detail(multi, "type"), "a tag begun on an earlier line: its element read from above");

        Check.Equal("<input type=\"email|\"", Apply("<input type=\"|\"", (await Complete(html, "<input type=\"|\"", '"'))!, "email"), "input's type values");
        Check.Equal("<button type=\"submit\"|", Apply("<button type=|", (await Complete(html, "<button type=|", '='))!, "submit"), "after '=', the value with its quotes");
        Check.True(Has(await Complete(html, "<a target=\"|\"", '"'), "_blank"), "target");
        Check.True(Has(await Complete(html, "<link rel=\"|\"", '"'), "stylesheet"), "rel");
        Check.True(Has(await Complete(html, "<p dir=\"|\"", '"'), "rtl"), "dir");
        Check.True(Has(await Complete(html, "<input disabled=\"|\"", '"'), "disabled"), "a boolean's value");
        Check.True(await Complete(html, "<p class=\"|\"", '"') is null, "a free value offers nothing");
        Check.True(await Complete(html, "<p>some text |", ' ') is null, "text offers nothing");
    }

    [Test]
    public static async Task EndTagsAndEntitiesComplete()
    {
        var html = HtmlTokenizer.For(HtmlOptions.Html);
        var open = await Complete(html, "<ul>\n  <li>a\n  <li>b\n</|", '/');
        Check.True(open?.Items is [{ Label: "</li>" }, { Label: "</ul>" }, ..], $"after '</', the open elements, the innermost first (an <li> implied closed by the next) ({Labels(open)})");
        Check.Equal("<div><span>x</span>\n</div>|", Apply("<div><span>x</span>\n</|", (await Complete(html, "<div><span>x</span>\n</|", '/'))!, "</div>"), "the innermost open element closes");
        Check.Equal("<div>\n</div>|", Apply("<div>\n</|>", (await Complete(html, "<div>\n</|>", '/'))!, "</div>"), "an auto-closed '>' is taken, not doubled");
        Check.Equal("<div>\n</div>|", Apply("<div>\n</di|", (await Complete(html, "<div>\n</di|", CompletionTrigger.Invoke))!, "</div>"), "a typed name still offers its element");

        var end = await Complete(html, "<section class=\"a\">|", '>');
        Check.True(end?.Items is [{ Label: "</section>" }], $"a start tag's '>' offers its end tag ({Labels(end)})");
        Check.Equal("<section class=\"a\">|</section>", Apply("<section class=\"a\">|", end!, "</section>"), "inserted after the caret, which stays");
        Check.True(await Complete(html, "<br>|", '>') is null, "a void element has none");
        Check.True(await Complete(html, "<p>|</p>", '>') is null, "nor one already closed");

        var entities = await Complete(html, "a &am|", CompletionTrigger.Invoke);
        Check.Equal("a &amp;|", Apply("a &am|", entities!, "&amp;"), "an entity after '&'");
        Check.True(Has(await Complete(html, "x &|", '&'), "&nbsp;"), "nbsp among them");
    }

    [Test]
    public static async Task CssCompletesInStyleAndInStyleAttributes()
    {
        var html = HtmlTokenizer.For(HtmlOptions.Html);
        var inStyle = await Complete(html, "<style>\n  :root { --brand: red; }\n  body { col| }\n</style>", CompletionTrigger.Invoke);
        Check.True(Has(inStyle, "color") && Has(inStyle, "--brand"), $"a <style>'s rule body: properties and the file's custom properties ({Labels(inStyle)})");
        Check.Equal("<style>\n  :root { --brand: red; }\n  body { color: | }\n</style>", Apply("<style>\n  :root { --brand: red; }\n  body { col| }\n</style>", inStyle!, "color"), "a property committed with its ': '");
        Check.True(Has(await Complete(html, "<p style=\"margin: 0; colo|\">", CompletionTrigger.Invoke), "color"), "style=\"…\": declarations");
        Check.True(Has(await Complete(html, "<p style=\"display: |\">", ' '), "flex"), "and their values");
    }

    [Test]
    public static async Task CssPropertiesValuesAtRulesAndPseudosComplete()
    {
        var css = StyleTokenizer.For(StyleDialect.Css);
        const string root = ":root { --brand: oklch(0.6 0.2 30); --gap: 4px; }\n";
        var properties = await Complete(css, root + ".a { col| }", CompletionTrigger.Invoke);
        Check.True(Has(properties, "color") && Has(properties, "column-gap") && Has(properties, "--brand"), $"a rule's body: properties and custom properties ({Labels(properties)})");
        Check.Equal(root + ".a { color: | }", Apply(root + ".a { col| }", properties!, "color"), "committed with ': '");
        Check.Equal(root + ".a { color|: red }", Apply(root + ".a { col|: red }", (await Complete(css, root + ".a { col|: red }", CompletionTrigger.Invoke))!, "color"), "no second ':' when one follows");

        var values = await Complete(css, root + ".a { color: | }", ':');
        Check.True(Has(values, "oklch()") && Has(values, "color-mix()") && Has(values, "currentcolor") && Has(values, "rebeccapurple") && Has(values, "inherit") && Has(values, "var()") && Has(values, "clamp()"),
            $"a colour's values: functions, keywords, named colours, the CSS-wide keywords ({Labels(values)})");
        Check.Equal(root + ".a { color: oklch(|) }", Apply(root + ".a { color: | }", values!, "oklch()"), "a function: the caret inside it");
        Check.True(Has(await Complete(css, ".a { display: f| }", CompletionTrigger.Invoke), "flex"), "display's keywords");
        var custom = await Complete(css, root + ".a { gap: var(--| }", '(');
        Check.True(Has(custom, "--brand") && Has(custom, "--gap"), $"var(): the file's custom properties ({Labels(custom)})");

        var rules = await Complete(css, "@|", '@');
        Check.True(Has(rules, "@media") && Has(rules, "@container") && Has(rules, "@layer") && !Has(rules, "@mixin"), $"at-rules, CSS's own ({Labels(rules)})");
        Check.True(Has(await Complete(StyleTokenizer.For(StyleDialect.Scss), "@|", '@'), "@mixin"), "SCSS's at-rules in SCSS");
        var old = await Complete(StyleTokenizer.For(StyleDialect.Css, 2022), "@|", '@');
        Check.True(Has(old, "@layer") && !Has(old, "@container"), "an older Baseline year leaves out what is newer (@container 2023)");

        Check.True(Has(await Complete(css, "a:h|", CompletionTrigger.Invoke), ":hover"), "a pseudo-class after ':'");
        Check.True(Has(await Complete(css, "a::be|", CompletionTrigger.Invoke), "::before"), "a pseudo-element after '::'");
        Check.Equal("li:not(|)", Apply("li:|", (await Complete(css, "li:|", ':'))!, ":not"), "a functional pseudo-class: the caret inside");
        Check.True(Has(await Complete(css, "@media (width > 600px) {\n  a:|", ':'), ":focus-visible"), "inside @media a statement is a selector");
        Check.True(await Complete(css, "/* col| */", CompletionTrigger.Invoke) is null, "nothing in a comment");
        Check.True(await Complete(css, ".a { content: \"col|\" }", CompletionTrigger.Invoke) is null, "nothing in a string");

        var scss = StyleTokenizer.For(StyleDialect.Scss);
        const string sheet = "$primary: red;\n$space: 4px;\n@mixin card($p) { padding: $p; }\n.a {\n  color: $|\n}";
        var variables = await Complete(scss, sheet, '$');
        Check.True(Has(variables, "$primary") && Has(variables, "$space"), $"SCSS: the file's $variables ({Labels(variables)})");
        var mixins = await Complete(scss, "@mixin card($p) { padding: $p; }\n.a {\n  @include |\n}", ' ');
        Check.True(Has(mixins, "card"), $"SCSS: @include offers the file's mixins ({Labels(mixins)})");
        Check.True(Has(await Complete(StyleTokenizer.For(StyleDialect.Less), ".a { marg| }", CompletionTrigger.Invoke), "margin"), "LESS: properties");
    }

    [Test]
    public static async Task AngularBlocksAndBindingsComplete()
    {
        var angular = HtmlTokenizer.For(HtmlOptions.AngularTemplate);
        var blocks = await Complete(angular, "<div>\n  @|", '@');
        Check.True(blocks?.Items.FirstOrDefault(i => i.Label == "@if") is { Kind: CompletionItemKind.Snippet, Data: Snippet { Body: var body } } && body.Contains("${1:condition}", StringComparison.Ordinal)
            && Has(blocks, "@for") && Has(blocks, "@switch") && Has(blocks, "@defer") && Has(blocks, "@let"), $"'@': the blocks as snippets ({Labels(blocks)})");
        Check.True(!Has(await Complete(HtmlTokenizer.For(HtmlOptions.AngularTemplate with { AngularVersion = 1703 }), "@|", '@'), "@let"), "Angular 17: no @let");
        Check.True(await Complete(HtmlTokenizer.For(HtmlOptions.AngularTemplate with { AngularVersion = 1602 }), "@|", '@') is null, "Angular 16: no blocks");
        Check.True(await Complete(HtmlTokenizer.For(HtmlOptions.Html), "@|", '@') is null, "plain HTML: none");

        var properties = await Complete(angular, "<input [|]", '[');
        Check.True(Has(properties, "value") && Has(properties, "ngModel") && Has(properties, "disabled"), $"'[': the element's properties ({Labels(properties)})");
        Check.Equal("<input [value]=\"|\"", Apply("<input [|]", properties!, "value"), "a property binding, the auto-closed ']' taken");
        Check.Equal("<button (click)=\"|\"", Apply("<button (|)", (await Complete(angular, "<button (|)", '('))!, "click"), "an event binding");
        Check.Equal("<input [(ngModel)]=\"|\"", Apply("<input [(|)]", (await Complete(angular, "<input [(|)]", '('))!, "ngModel"), "a two-way binding");
        Check.Equal("<li *ngFor=\"|\"", Apply("<li *|", (await Complete(angular, "<li *|", '*'))!, "*ngFor"), "a structural directive");
    }

    [Test]
    public static async Task TheEditorOpensTheListOnTriggersAndTheSettingsTurnItOff()
    {
        var options = new CraftOptions { CompletionDelayMilliseconds = 0, InlineCompletionProvider = "none" };
        var language = new HtmlLanguage("html-completion-test", [".html"]);
        var (model, document) = RealEditors.Make("", options, language);
        using var _m = model;
        using var _d = document;
        var ui = new ConcurrentQueue<Action>();
        model.Dispatch = ui.Enqueue;
        Check.True(model.CompletingSession is not null && model.SemanticSession is null, "HTML completes without a semantic layer (no rename, references or outline)");
        model.OnText("<");
        await WaitOpen(model, ui);
        Check.True(model.Completion.IsOpen && model.Completion.Items.Any(i => i.Label == "div"), "'<' opens the list");
        model.OnText("d");
        model.OnText("i");
        model.OnText("v");
        Check.True(model.Completion.IsOpen && model.Completion.Selected?.Label == "div", $"typing filters it ({model.Completion.Selected?.Label})");
        model.OnKey(EditorKey.Enter, KeyModifiers.None);
        Check.Equal("<div|", RealEditors.Marked(model), "Enter commits");
        model.OnText(" ");
        await WaitOpen(model, ui);
        model.OnText("aria-l");
        Check.True(model.Completion.IsOpen && model.Completion.Selected?.Label == "aria-label", $"'-' is a word's character: the list filters through it ({model.Completion.Selected?.Label})");
        model.OnKey(EditorKey.Escape, KeyModifiers.None);

        language.Settings = static () => new HtmlSettings { Completion = false };
        var (off, offDocument) = RealEditors.Make("", options, language);
        using var _o = off;
        using var _od = offDocument;
        Check.True(off.CompletingSession is null, "completion off: nothing completes");
        Check.Equal("CRAFT_PLUGIN_HTML_COMPLETION", PluginSetting.VariableName(HtmlParts.LanguageName, "completion"), "HTML's variable");
        Check.Equal("CRAFT_PLUGIN_CSS_COMPLETION", PluginSetting.VariableName(CssPlugin.PluginName, "completion"), "CSS's variable");
        Check.True(HtmlSettings.Declared.Any(s => s.Key == "completion" && s.Shape is { Section: SettingSections.Editor }) && CssSettings.Declared.Any(s => s.Key == "completion" && s.Shape is { Section: SettingSections.Editor })
            && AngularSettings.Declared.Any(s => s.Key == "completion"), "each plug-in's completion toggle is on the settings page");
        Check.True(CssSettings.Default.Completion && new HtmlSettings().Completion, "on by default");
        Check.True(StyleLanguages.All[0].Open(new PieceTableBuffer("a {}"), LanguageOpenContext.None) is LineStateSession { CanComplete: true }, "a CSS session completes");
    }

    [Test]
    public static async Task GhostTextStillWorksBesideTheList()
    {
        var provider = new FixedInline("Ghost text");
        InlineCompletionProviders.Register(provider);
        try
        {
            var options = new CraftOptions { CompletionDelayMilliseconds = 0, InlineCompletionProvider = provider.Name };
            var (model, document) = RealEditors.Make("<p>", options, new HtmlLanguage("html-ghost-test", [".html"]));
            using var _m = model;
            using var _d = document;
            var ui = new ConcurrentQueue<Action>();
            model.Dispatch = ui.Enqueue;
            model.Select(3, 3);
            Check.True(model.InlineCompletion.Invoke(), "ghost text is asked in HTML");
            await WaitUntil(ui, () => model.InlineCompletion.IsShown);
            Check.True(model.InlineCompletion.IsShown, "it shows");
            model.OnText("<");
            await WaitOpen(model, ui);
            Check.True(model.Completion.IsOpen && !model.InlineCompletion.IsShown, "while the list is open the ghost text hides");
            model.OnKey(EditorKey.Escape, KeyModifiers.None);
            Check.True(model.InlineCompletion.Invoke(), "and is asked again after it");
            await WaitUntil(ui, () => model.InlineCompletion.IsShown);
            Check.True(model.InlineCompletion.IsShown, "showing beside the language's completion");
        }
        finally
        {
            InlineCompletionProviders.Unregister(provider.Name);
        }
    }

    [Test]
    public static async Task TheTimeToTheListIsMeasured()
    {
        var page = new StringBuilder();
        while (page.ToString().Count(c => c == '\n') < 2000)
        {
            page.Append(WebMarkupTests.Page).Append('\n');
        }

        string htmlText = page + "<main>\n  <section class=\"x\">\n    <input ";
        var sheet = new StringBuilder(":root { --brand: oklch(0.6 0.2 30); }\n");
        for (int i = 0; i < 9_999; i++)
        {
            sheet.Append(".c").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" { color: var(--brand); margin: 0 4px; }\n");
        }

        int cssLines = sheet.ToString().Count(c => c == '\n');
        string cssText = sheet + ".z { color: ";
        var html = HtmlTokenizer.For(HtmlOptions.Html);
        var css = StyleTokenizer.For(StyleDialect.Css);
        var (htmlCold, htmlWarm, htmlItems, htmlBytes) = await Time(html, HtmlCompletion.Instance, htmlText, ' ');
        var (closeCold, closeWarm, closeItems, closeBytes) = await Time(html, HtmlCompletion.Instance, page + "<main>\n  <section>\n</", '/');
        var (cssCold, cssWarm, cssItems, cssBytes) = await Time(css, StyleCompletion.Instance, cssText, ':');
        Check.True(htmlItems > 100 && closeItems >= 2 && cssItems > 100, $"lists made ({htmlItems}, {closeItems}, {cssItems})");
        string report = string.Create(CultureInfo.InvariantCulture, $"""
            Web completion bench (test WebCompletion.TheTimeToTheListIsMeasured), {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC, {Environment.ProcessorCount} cores, .NET {Environment.Version}
            Through LineStateSession.GetCompletionsAsync (the thread pool), the caret's line state known; "cold" is a session's first ask.
            {htmlText.Count(c => c == '\n'):N0}-line HTML, the caret in a start tag at the end (attributes):  cold {htmlCold,7:F2} ms, warm {htmlWarm,7:F3} ms, {htmlItems} items, {htmlBytes:N0} B a list
            the same page, "</" at the end (the document read for the open elements):  cold {closeCold,7:F2} ms, warm {closeWarm,7:F3} ms, {closeItems} items, {closeBytes:N0} B a list
            {cssLines:N0}-line CSS, a colour's value at the end (the sheet read for its blocks):     cold {cssCold,7:F2} ms, warm {cssWarm,7:F3} ms, {cssItems} items, {cssBytes:N0} B a list

            """);
        string folder = TestRunner.RepositoryPath("test-output", "benches");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "web-completion.txt"), report);
        Console.WriteLine(report);
    }

    // ---- helpers ----

    // The list at the '|' of `marked` in a session of `tokenizer` with `source` (the language's own completion when null).
    private static Task<CompletionList?> Complete(LineTokenizer tokenizer, string marked, char typed) => Complete(tokenizer, marked, CompletionTrigger.Typed(typed));

    private static async Task<CompletionList?> Complete(LineTokenizer tokenizer, string marked, CompletionTrigger trigger)
    {
        int caret = marked.IndexOf('|', StringComparison.Ordinal);
        string text = marked.Remove(caret, 1);
        ILineCompletion source = tokenizer is StyleTokenizer ? StyleCompletion.Instance : HtmlCompletion.Instance;
        var buffer = new PieceTableBuffer(text);
        using var session = new LineStateSession(buffer, tokenizer, null, LanguageSettings.Default, source);
        return await session.GetCompletionsAsync(buffer.Current, caret, trigger, CancellationToken.None);
    }

    // The text after committing the item labelled `label`, with '|' at the caret.
    private static string Apply(string marked, CompletionList list, string label)
    {
        int caret = marked.IndexOf('|', StringComparison.Ordinal);
        string text = marked.Remove(caret, 1);
        var item = list.Items.FirstOrDefault(i => i.Label == label) ?? throw new InvalidOperationException($"no item \"{label}\" in {string.Join(", ", list.Items.Select(i => i.Label).Take(40))}");
        var edit = item.Edit;
        string after = text[..edit.Start] + edit.Text + text[edit.End..];
        int at = item.CaretAfter >= 0 ? item.CaretAfter : edit.Start + edit.Text.Length;
        return after.Insert(at, "|");
    }

    private static bool Has(CompletionList? list, string label) => list?.Items.Any(i => i.Label == label) == true;

    private static string? Detail(CompletionList? list, string label) => list?.Items.FirstOrDefault(i => i.Label == label)?.Detail;

    private static string Labels(CompletionList? list) => list is null ? "no list" : string.Join(", ", list.Items.Select(i => i.Label).Take(30));

    private static async Task<(double Cold, double Warm, int Items, long Bytes)> Time(LineTokenizer tokenizer, ILineCompletion source, string text, char typed)
    {
        var buffer = new PieceTableBuffer(text);
        using var session = new LineStateSession(buffer, tokenizer, null, LanguageSettings.Default, source);
        long start = Stopwatch.GetTimestamp();
        var list = await session.GetCompletionsAsync(buffer.Current, text.Length, CompletionTrigger.Typed(typed), CancellationToken.None);
        double cold = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        const int Rounds = 50;
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < Rounds; i++)
        {
            list = await session.GetCompletionsAsync(buffer.Current, text.Length, CompletionTrigger.Typed(typed), CancellationToken.None);
        }

        double warm = Stopwatch.GetElapsedTime(start).TotalMilliseconds / Rounds;

        // What one list costs in memory, made on this thread (the items are most of it).
        var line = buffer.Current.GetLine(buffer.Current.LineCount - 1);
        var request = new LineCompletionRequest(buffer.Current, text.Length, CompletionTrigger.Typed(typed), tokenizer, StateAt(tokenizer, buffer.Current, line));
        source.Complete(request, CancellationToken.None);
        long before = GC.GetAllocatedBytesForCurrentThread();
        source.Complete(request, CancellationToken.None);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        return (cold, warm, list?.Items.Count ?? 0, bytes);
    }

    private static ulong StateAt(LineTokenizer tokenizer, ITextSnapshot snapshot, TextLine target)
    {
        ulong state = tokenizer.Initial;
        for (int i = 0; i < snapshot.LineCount && snapshot.GetLine(i).Start < target.Start; i++)
        {
            var line = snapshot.GetLine(i);
            state = tokenizer.Tokenize(snapshot.GetText(line.Start, line.Length), line.Start, state, null);
        }

        return state;
    }

    private static async Task WaitOpen(EditorModel model, ConcurrentQueue<Action> ui) => await WaitUntil(ui, () => model.Completion.IsOpen);

    private static async Task WaitUntil(ConcurrentQueue<Action> ui, Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done() && watch.ElapsedMilliseconds < 5000)
        {
            while (ui.TryDequeue(out var action))
            {
                action();
            }

            if (!done())
            {
                await Task.Delay(2);
            }
        }
    }

    /// <summary>An inline completion provider that answers its text at once.</summary>
    private sealed class FixedInline(string text) : IInlineCompletionProvider
    {
        public string Name => "web-completion-ghost-test";

        public string Summary => "Test: a fixed completion";

        public int DelayMilliseconds => 0;

        public bool Accepts(InlineCompletionRequest request) => true;

        public async IAsyncEnumerable<InlineCompletionPiece> CompleteAsync(InlineCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield return new InlineCompletionPiece(text);
        }
    }
}
