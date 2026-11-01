// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Text;
using Craft.Editor;
using Craft.Plugins.Angular;
using Craft.Plugins.Html;
using Craft.Plugins.TypeScript;
using Craft.Tests.Runner;
using Craft.Web.Markup;
using Craft.Web.Script;
using Craft.Web.Style;
using Idrak.Abstraction.Plugins;
using static Craft.Tests.ProjectLanguageTests;
using static Craft.Tests.WebMarkupTests;

namespace Craft.Tests;

/// <summary>
/// Phase W's W-markup, Angular: every control flow block, binding, event and pipe, <c>@let</c>, <c>@defer</c>'s triggers,
/// <c>*ngIf</c>/<c>*ngFor</c> microsyntax, the version gate (17 and 18.1), inline templates and styles in an Angular
/// project's TypeScript, the language chosen by angular.json and by name, the settings, a session switched when the
/// project's Angular is read, angular.json read again when the workspace changes, and the formatter and folding of blocks.
/// </summary>
[TestGroup("WebAngular")]
public static class WebAngularTests
{
    internal const string Template = """
        <app-header [title]="title | uppercase" (menu)="toggle($event)" [(value)]="name" #header></app-header>
        <li *ngFor="let item of items; trackBy: byId; let i = index">{{ i }}: {{ item.name | slice:0:10 }}</li>
        <p *ngIf="user?.name as n; else none">{{ n! }}</p>
        @if (user.isLoggedIn; as u) {
          <span>{{ u.name }}</span>
        } @else if (loading) {
          <spinner />
        } @else {
          <a bind-href="url" on-click="go()">Sign in</a>
        }
        @for (item of items; track item.id; let i = $index, e = $even) {
          <li [class.even]="e">{{ i }} {{ item }}</li>
        } @empty {
          <li>None</li>
        }
        @switch (mode) {
          @case ('a') { <b>A</b> }
          @default { <i>?</i> }
        }
        @defer (on viewport; prefetch on idle) {
          <heavy-cmp />
        } @placeholder (minimum 500ms) {
          <p>wait</p>
        } @loading (after 100ms; minimum 1s) {
          <p>Loading</p>
        } @error {
          <p>Failed</p>
        }
        @let total = price * qty;
        <p title="Total: {{ total | currency }}">{{ `${total}` }}</p>
        """;

    [Test]
    public static void EveryBlockBindingAndPipeHasItsColours()
    {
        var spans = Spans(HtmlTokenizer.For(HtmlOptions.AngularTemplate), Template);
        Expect(spans, "app-header", TokenClasses.ClassName, "a component's tag");
        Expect(spans, "title", TokenClasses.PropertyName, "a property binding [title]");
        Expect(spans, "uppercase", TokenClasses.MethodName, "a pipe");
        Expect(spans, "menu", TokenClasses.EventName, "an event binding (menu)");
        Expect(spans, "toggle", TokenClasses.MethodName, "a statement's call");
        Expect(spans, "[(", TokenClasses.Punctuation, "a two-way binding's [(");
        Expect(spans, "value", TokenClasses.PropertyName, "its property");
        Expect(spans, "#header", TokenClasses.LocalName, "a template reference");
        Expect(spans, "*ngFor", TokenClasses.ControlKeyword, "a structural directive");
        Expect(spans, "let", TokenClasses.Keyword, "microsyntax's let");
        Expect(spans, "of", TokenClasses.Keyword, "microsyntax's of");
        Expect(spans, "{{", TokenClasses.Punctuation, "interpolation");
        Expect(spans, "slice", TokenClasses.MethodName, "a pipe with arguments");
        Expect(spans, "?.", TokenClasses.Operator, "safe navigation");
        Expect(spans, "as", TokenClasses.Keyword, "*ngIf's as");
        Expect(spans, "!", TokenClasses.Operator, "a non-null assertion");
        Expect(spans, "@if", TokenClasses.ControlKeyword, "@if");
        Expect(spans, "isLoggedIn", TokenClasses.PropertyName, "the condition's expression");
        Expect(spans, "@else", TokenClasses.ControlKeyword, "@else");
        Expect(spans, "if", TokenClasses.ControlKeyword, "@else if");
        Expect(spans, "bind-href", TokenClasses.PropertyName, "bind-");
        Expect(spans, "on-click", TokenClasses.EventName, "on-");
        Expect(spans, "@for", TokenClasses.ControlKeyword, "@for");
        Expect(spans, "track", TokenClasses.Keyword, "track");
        Expect(spans, "class.even", TokenClasses.PropertyName, "a class binding");
        Expect(spans, "@empty", TokenClasses.ControlKeyword, "@empty");
        Expect(spans, "@switch", TokenClasses.ControlKeyword, "@switch");
        Expect(spans, "@case", TokenClasses.ControlKeyword, "@case");
        Expect(spans, "'a'", TokenClasses.String, "a case's value");
        Expect(spans, "@default", TokenClasses.ControlKeyword, "@default");
        Expect(spans, "@defer", TokenClasses.ControlKeyword, "@defer");
        Expect(spans, "on", TokenClasses.Keyword, "a trigger's on");
        Expect(spans, "viewport", TokenClasses.Keyword, "on viewport");
        Expect(spans, "prefetch", TokenClasses.Keyword, "prefetch");
        Expect(spans, "idle", TokenClasses.Keyword, "prefetch on idle");
        Expect(spans, "@placeholder", TokenClasses.ControlKeyword, "@placeholder");
        Expect(spans, "minimum", TokenClasses.Keyword, "minimum");
        Expect(spans, "500ms", TokenClasses.Number, "a duration");
        Expect(spans, "@loading", TokenClasses.ControlKeyword, "@loading");
        Expect(spans, "after", TokenClasses.Keyword, "after");
        Expect(spans, "@error", TokenClasses.ControlKeyword, "@error");
        Expect(spans, "@let", TokenClasses.ControlKeyword, "@let");
        Expect(spans, "total", TokenClasses.LocalName, "@let's name");
        Expect(spans, "currency", TokenClasses.MethodName, "a pipe inside an attribute's {{ }}");
        Expect(spans, "${", TokenClasses.Punctuation, "a template literal in an expression");
        var blocks = Spans(HtmlTokenizer.For(HtmlOptions.AngularTemplate), Template[..Template.IndexOf("@let", StringComparison.Ordinal)]);
        Check.True(blocks.Count(s => s.Text == "{" && s.Class == TokenClasses.Punctuation) == 12 && blocks.Count(s => s.Text == "}" && s.Class == TokenClasses.Punctuation) == 12,
            "every block's { is closed by a } in text (12 blocks)");
        var last = SpansAt(HtmlTokenizer.For(HtmlOptions.AngularTemplate), Template).Last();
        Check.Equal(TokenClasses.Punctuation, last.Class, "the state is right to the end (the last </p>'s >)");
    }

    [Test]
    public static void TheVersionGateReadsBlocksAsTextBeforeTheirVersion()
    {
        const string text = "@if (x) {\n  <p>a</p>\n}\n@let a = 1;\n";
        var v16 = Spans(HtmlTokenizer.For(HtmlOptions.AngularTemplate with { AngularVersion = 1602 }), text);
        Check.True(!v16.Any(s => s.Class == TokenClasses.ControlKeyword) && !v16.Any(s => s.Text == "}" && s.Class == TokenClasses.Punctuation), "before Angular 17, @if and its braces are text");
        var v17 = Spans(HtmlTokenizer.For(HtmlOptions.AngularTemplate with { AngularVersion = 1703 }), text);
        Check.True(v17.Any(s => s.Text == "@if" && s.Class == TokenClasses.ControlKeyword) && !v17.Any(s => s.Text == "@let" && s.Class == TokenClasses.ControlKeyword), "17: @if is a block, @let is text");
        var v18 = Spans(HtmlTokenizer.For(HtmlOptions.AngularTemplate with { AngularVersion = 1801 }), text);
        Check.True(v18.Any(s => s.Text == "@let" && s.Class == TokenClasses.ControlKeyword), "18.1: @let");
        Check.Equal(1703, new AngularSettings { Version = "17.3" }.VersionFor(null), "a set version");
        Check.Equal(ScriptVersion.Latest, new AngularSettings { ProjectVersion = false }.VersionFor(new AngularWorkspace("x", [], 1602)), "projectVersion off: the latest");
        Check.Equal(1602, new AngularSettings().VersionFor(new AngularWorkspace("x", [], 1602)), "else the project's");
        var plain = Spans(HtmlTokenizer.For(HtmlOptions.Html), "@if (x) {\n<p>{{ a }}</p>\n}");
        Check.True(!plain.Any(s => s.Class == TokenClasses.ControlKeyword || s.Text == "{{"), "plain HTML reads no Angular");
    }

    [Test]
    public static void InlineTemplatesAndStylesInAnAngularProjectsTypeScript()
    {
        const string text = """
            @Component({
              selector: 'app-root',
              template: `
                <h1 [title]="name">{{ name | titlecase }}</h1>
                @if (show) { <p>Hi</p> }
              `,
              styles: [`h1 { color: red; }`, `p { margin: 0 }`],
            })
            export class AppComponent {
              template = 'not a template';
              name = 'x' satisfies string;
            }
            """;
        var tokenizer = AngularTypeScriptTokenizer.For(ScriptVersion.Latest, StyleDialect.Css);
        var spans = Spans(tokenizer, text);
        Expect(spans, "@Component", TokenClasses.PreprocessorKeyword, "TypeScript's decorator");
        Expect(spans, "'app-root'", TokenClasses.String, "a string of TypeScript");
        Expect(spans, "h1", TokenClasses.Keyword, "the inline template's tag");
        Expect(spans, "title", TokenClasses.PropertyName, "its binding");
        Expect(spans, "titlecase", TokenClasses.MethodName, "its pipe");
        Expect(spans, "@if", TokenClasses.ControlKeyword, "its block");
        Expect(spans, "color", TokenClasses.PropertyName, "the first inline style");
        Expect(spans, "margin", TokenClasses.PropertyName, "the second literal of styles: [ ]");
        Expect(spans, "export", TokenClasses.Keyword, "TypeScript again after the template and styles");
        Expect(spans, "'not a template'", TokenClasses.String, "template = '…' (no ':') is TypeScript's");
        Expect(spans, "satisfies", TokenClasses.Keyword, "TypeScript's words");

        var scss = Spans(AngularTypeScriptTokenizer.For(ScriptVersion.Latest, StyleDialect.Scss), "@Component({\n  styles: `$x: 1px;\n    .a { width: $x; }`,\n})\nclass A {}");
        Expect(scss, "$x", TokenClasses.LocalName, "an SCSS project's inline styles read SCSS, over lines");
        Expect(scss, "class", TokenClasses.Keyword, "and TypeScript after");

        var buffer = new PieceTableBuffer(text);
        using var session = new LineStateSession(buffer, tokenizer, null, LanguageSettings.Default);
        Check.True(session.RulesAt(buffer.Current, text.IndexOf("<h1", StringComparison.Ordinal)) is { BlockCommentOpen: "<!--" }, "inside the template, HTML's comments");
        Check.True(session.RulesAt(buffer.Current, 0) is null, "in the TypeScript, its own rules");
    }

    [Test]
    public static async Task TheLanguageIsChosenByAngularJsonByNameAndBySettings()
    {
        using var temp = new TempFolder();
        string root = temp.Path;
        Directory.CreateDirectory(Path.Combine(root, "src", "app"));
        Directory.CreateDirectory(Path.Combine(root, "projects", "lib", "src"));
        Directory.CreateDirectory(Path.Combine(root, "node_modules", "@angular", "core"));
        await File.WriteAllTextAsync(Path.Combine(root, "angular.json"), """
            { "projects": {
                "app": { "root": "", "sourceRoot": "src", "architect": { "build": { "options": { "inlineStyleLanguage": "scss" } } } },
                "lib": { "root": "projects/lib", "schematics": { "@schematics/angular:component": { "style": "less" } } }
            } }
            """);
        await File.WriteAllTextAsync(Path.Combine(root, "node_modules", "@angular", "core", "package.json"), "{ \"name\": \"@angular/core\", \"version\": \"18.0.1\" }");
        await File.WriteAllTextAsync(Path.Combine(root, "package.json"), "{ \"dependencies\": { \"@angular/core\": \"^19.2.0\" } }");
        string other = Path.Combine(root, "..", Path.GetFileName(root) + "-plain");
        Directory.CreateDirectory(other);
        AngularWorkspaces.Forget();
        IdrakPlugins.Register(new HtmlPlugin());
        IdrakPlugins.Register(new TypeScriptPlugin());
        IdrakPlugins.Register(new AngularPlugin());
        try
        {
            await LanguageSelectors.PrepareAsync(root);
            var workspace = AngularWorkspaces.Find(root)!;
            Check.True(workspace is { Version: 1800, Projects.Count: 2 }, $"angular.json's projects and the installed @angular/core (18.0.1 over package.json's ^19.2.0) ({workspace})");
            string appHtml = Path.Combine(root, "src", "app", "app.html");
            string appTs = Path.Combine(root, "src", "app", "app.ts");
            string libTs = Path.Combine(root, "projects", "lib", "src", "lib.ts");
            Check.Equal("angular-html", EditorLanguages.ForFile(appHtml, root)?.Name, "a project's .html (Angular 20's app.html) is an Angular template");
            Check.Equal("angular-typescript", EditorLanguages.ForFile(appTs, root)?.Name, "a project's .ts reads its inline templates");
            Check.Equal("typescript", EditorLanguages.ForFile(Path.Combine(root, "src", "types.d.ts"), root)?.Name, "a .d.ts stays TypeScript");
            Check.Equal("angular-html", EditorLanguages.ForFile("x.component.html")?.Name, "a *.component.html by its name, with no workspace");
            Check.Equal("html", EditorLanguages.ForFile(Path.Combine(other, "page.html"), other)?.Name, "a folder with no angular.json: HTML");

            using (var app = (LineStateSession)AngularParts.TypeScript.Open(new PieceTableBuffer("class A {}"), new LanguageOpenContext(appTs, root, LanguageSettings.Default)))
            using (var lib = (LineStateSession)AngularParts.TypeScript.Open(new PieceTableBuffer("class B {}"), new LanguageOpenContext(libTs, root, LanguageSettings.Default)))
            {
                Check.True(app.Tokenizer is AngularTypeScriptTokenizer { StyleDialect: StyleDialect.Scss, AngularVersion: 1800 }, "the app's inline styles are SCSS (inlineStyleLanguage), Angular 18.0");
                Check.True(lib.Tokenizer is AngularTypeScriptTokenizer { StyleDialect: StyleDialect.Less }, "the library's are LESS (its component schematic), the longest root wins");
            }

            using (var html = (LineStateSession)AngularParts.Templates.Open(new PieceTableBuffer("@if (a) { <p>x</p> }\n@let b = 1;"), new LanguageOpenContext(appHtml, root, LanguageSettings.Default)))
            {
                Check.True(html.Tokenizer is HtmlTokenizer { Options.AngularVersion: 1800 }, "the template reads the project's Angular 18.0");
            }

            AngularWorkspaces.Forget();
            int reclassified = 0;
            using (var fresh = (LineStateSession)AngularParts.Templates.Open(new PieceTableBuffer("<p>x</p>"), new LanguageOpenContext(appHtml, root, LanguageSettings.Default)))
            {
                fresh.Reclassified += (_, _) => reclassified++;
                Check.True(fresh.Tokenizer is HtmlTokenizer { Options.AngularVersion: ScriptVersion.Latest }, "a template whose project was not read opens at once at the latest");
                await AngularParts.Templates.LastRead!;
                Check.True(fresh.Tokenizer is HtmlTokenizer { Options.AngularVersion: 1800 } && reclassified == 1, "then switches to the project's version through Reclassified");
            }

            AngularParts.Templates.Settings = static () => new AngularSettings { Templates = "suffix" };
            Check.Equal("html", EditorLanguages.ForFile(appHtml, root)?.Name, "templates = suffix: a project's app.html is HTML");
            Check.Equal("angular-html", EditorLanguages.ForFile(Path.Combine(root, "src", "app", "a.component.html"), root)?.Name, "and a *.component.html an Angular template");
            AngularParts.Templates.Settings = static () => new AngularSettings { Templates = "off", InlineTemplates = false };
            Check.Equal("html", EditorLanguages.ForFile(Path.Combine(root, "src", "app", "a.component.html"), root)?.Name, "templates = off: every .html is HTML");
            Check.Equal("typescript", EditorLanguages.ForFile(appTs, root)?.Name, "inlineTemplates off: a .ts is TypeScript");
            AngularParts.Templates.Settings = static () => new AngularSettings();

            Check.True(IdrakPlugins.Unregister(AngularParts.PluginName), "the Angular plug-in unregisters");
            Check.Equal("typescript", EditorLanguages.ForFile(appTs, root)?.Name, "removed, a project's .ts is TypeScript again");
            Check.Equal("html", EditorLanguages.ForFile(appHtml, root)?.Name, "and its .html HTML");
            Check.Equal("html", EditorLanguages.ForFile("x.component.html")?.Name, "a *.component.html too");

            // angular.json read again when the host says the workspace changed (the explorer's Refresh).
            IdrakPlugins.Register(new AngularPlugin());
            using var host = new EditorHost();
            host.RefreshPlugins();
            host.WorkspaceFolder = root;
            for (int wait = 0; wait < 250 && AngularWorkspaces.Find(root) is null; wait++)
            {
                await Task.Delay(20);   // the read the folder's opening started lands first
            }

            await File.WriteAllTextAsync(Path.Combine(root, "angular.json"), "{ \"projects\": { \"app\": { \"root\": \"\" }, \"admin\": { \"root\": \"projects/admin\" } } }");
            host.RefreshWorkspace();
            bool seen = false;
            for (int wait = 0; wait < 250 && !seen; wait++)
            {
                seen = AngularWorkspaces.Find(root) is { } read && read.Projects.Any(p => p.Name == "admin");
                if (!seen)
                {
                    await Task.Delay(20);
                }
            }

            Check.True(seen, "after WorkspaceChanged (Refreshed), angular.json's new project is read off the UI thread");
        }
        finally
        {
            IdrakPlugins.Unregister(AngularParts.PluginName);
            IdrakPlugins.Unregister(TypeScriptLanguage.LanguageName);
            IdrakPlugins.Unregister(HtmlParts.LanguageName);
            AngularParts.Templates.Settings = static () => new AngularSettings();
            AngularWorkspaces.Forget();
            Directory.Delete(other, recursive: true);
        }
    }

    [Test]
    public static void BlocksFormatAndFold()
    {
        var formatter = new HtmlFormatter(HtmlTokenizer.For(HtmlOptions.AngularTemplate));
        var style = new FormatStyle("  ", "\n");
        const string messy = "<div>\n@if(user){\n<p   [title]=\"t\">{{ user.name }}</p>\n}@else{\n<p>none</p>\n}\n@for (i of items; track i) {\n<li>{{i}}</li>\n}\n@let x = 1;\n</div>\n";
        const string formatted = "<div>\n  @if (user) {\n    <p [title]=\"t\">{{ user.name }}</p>\n  } @else {\n    <p>none</p>\n  }\n  @for (i of items; track i) {\n    <li>{{i}}</li>\n  }\n  @let x = 1;\n</div>\n";
        Check.Equal(formatted, Format(formatter, messy, style), "blocks indent their content, @if (…) { and } @else { spaced, interpolations kept");
        Check.Equal(formatted, Format(formatter, formatted, style), "idempotent");
        Check.Equal(Strip(Template), Strip(Format(formatter, Template, style)), "only white space changed in the full template");
        string once = Format(formatter, Template, style);
        Check.Equal(once, Format(formatter, once, style), "and idempotent");
        foreach (var (bad, what) in new[] { ("@if (x) {\n<p>a</p>\n", "block's '{' is not closed"), ("<p>{{ a </p>\n", "'{{' is not closed"), ("<div>\n@if (x) {\n</div>\n}\n", "closes nothing open") })
        {
            var error = Check.Throws<FormatException>(() => Format(formatter, bad, style), $"malformed is refused: {bad.Replace("\n", "\\n", StringComparison.Ordinal)}");
            Check.True(error.Message.Contains(what, StringComparison.Ordinal), $"said ({error.Message})");
        }

        var ranges = HtmlFolding.Ranges(HtmlTokenizer.For(HtmlOptions.AngularTemplate), new PieceTableBuffer("@if (a) {\n  <p>a</p>\n} @else {\n  <p>b</p>\n}\n").Current);
        Check.SequenceEqual(new FoldingRange[] { new(0, 1), new(2, 4) }, ranges.OrderBy(r => r.StartLine).ToArray(), "@if folds to the line before } @else {, @else to its }");
    }
}
