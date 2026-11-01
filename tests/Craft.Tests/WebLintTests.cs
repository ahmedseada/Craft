// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Loader;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Plugins;
using Craft.Core.Settings;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Plugins.Lint;
using Craft.Plugins.Npm;
using Craft.Tests.Editor;
using Craft.Tests.Runner;
using Idrak.Abstraction.Plugins;

namespace Craft.Tests;

/// <summary>
/// Phase W, W-tools: plug-in diagnostics (IEditorHost.PublishDiagnostics, merged per source, Problems for closed files),
/// the linter contract (Linters) with ESLint and TSLint, the document formatter contract (DocumentFormatters) with
/// Prettier, and the lint plug-in (plugins/Craft.Plugins.Lint): reports read as bytes, the detection matrix, a fix as one
/// diff and one undo step, saves joined and a newer run cancelling the older, the approval asked once per workspace and
/// program and taken back, Prettier only with a configuration. The real-ESLint test needs Node and an ESLint to link into
/// its project; without them it says why and ends.
/// </summary>
[TestGroup("WebLint")]
public static class WebLintTests
{
    private static readonly string NodeName = OperatingSystem.IsWindows() ? "node.exe" : "node";

    // ---- the reports, as bytes ----

    [Test]
    public static void EslintAndTslintReportsAreReadFromBytes()
    {
        string eslint = "﻿" + """
            [{"filePath":"/w/a.js","messages":[
              {"ruleId":"no-unused-vars","severity":2,"message":"'x' is assigned a value but never used.","line":1,"column":7,"messageId":"unusedVar","endLine":1,"endColumn":8,
               "suggestions":[{"messageId":"removeVar","data":{"varName":"x"},"fix":{"range":[0,11],"text":""},"desc":"Remove unused variable 'x'."}]},
              {"ruleId":"semi","severity":1,"message":"Missing semicolon.","line":1,"column":12,"endLine":2,"endColumn":1,"fix":{"range":[11,11],"text":";"}},
              {"ruleId":null,"fatal":true,"severity":2,"message":"Parsing error: Unexpected token é","line":3,"column":5}],
             "suppressedMessages":[],"errorCount":2,"fatalErrorCount":1,"warningCount":1,"source":"const x = 1\nlet é = \"ü\"\n","usedDeprecatedRules":[{"ruleId":"semi","replacedBy":["@stylistic/semi"]}]},
             {"filePath":"/w/clean.js","messages":[],"errorCount":0}]
            """;
        var files = LintJson.ReadEslint(Encoding.UTF8.GetBytes(eslint));
        Check.SequenceEqual(["/w/a.js", "/w/clean.js"], [.. files.Select(f => f.Path)], "every file in the report's order, the clean one too (its empty list clears what it had)");
        var a = files[0];
        Check.True(a.Diagnostics[0] == new FileDiagnostic(1, 7, 1, 8, DiagnosticSeverity.Error, "no-unused-vars", "'x' is assigned a value but never used."),
            $"an error by line and column, its end exclusive ({a.Diagnostics[0]})");
        Check.True(a.Diagnostics[1] is { Severity: DiagnosticSeverity.Warning, Id: "semi", EndLine: 2, EndColumn: 1 }, "severity 1 is a warning; an end on the next line kept");
        Check.True(a.Diagnostics[2] is { Severity: DiagnosticSeverity.Error, Id: "", Line: 3, Column: 5, EndLine: 3, EndColumn: 5 } && a.Diagnostics[2].Message.StartsWith("Parsing error", StringComparison.Ordinal),
            "a fatal parse error: an error with no rule, a point where no end is given");
        Check.True(a.Fixes.Count == 2 && a.Fixes.Single(f => !f.Suggestion) is { Start: 11, Length: 0, Text: ";", Diagnostic: 1 } fix && fix.Title.Contains("semi", StringComparison.Ordinal),
            "the automatic fix: fix.range in UTF-16 units, for its diagnostic");
        Check.True(a.Fixes.Single(f => f.Suggestion) is { Start: 0, Length: 11, Text: "", Diagnostic: 0, Title: "Remove unused variable 'x'." }, "a suggestion, titled by its desc, applied only on its own");
        Check.Throws<FormatException>(() => LintJson.ReadEslint("{\"not\":\"an array\"}"u8), "a report that is not an array");
        Check.Throws<FormatException>(() => LintJson.ReadEslint("[{\"filePath\":"u8), "a cut report");

        var tslint = """
            [{"endPosition":{"character":13,"line":0,"position":13},"failure":"Missing semicolon","fix":{"innerStart":13,"innerLength":0,"innerText":";"},"name":"/w/b.ts","ruleName":"semicolon","ruleSeverity":"ERROR","startPosition":{"character":13,"line":0,"position":13}},
             {"endPosition":{"character":9,"line":2,"position":40},"failure":"Forbidden 'var' keyword","fix":[{"innerStart":31,"innerLength":3,"innerText":"let"},{"innerStart":38,"innerLength":0,"innerText":""}],"name":"/w/b.ts","ruleName":"no-var-keyword","ruleSeverity":"warning","startPosition":{"character":0,"line":2,"position":31}},
             {"endPosition":{"character":1,"line":0,"position":1},"failure":"x","name":"/w/c.ts","ruleName":"r","ruleSeverity":"OFF","startPosition":{"character":0,"line":0,"position":0}}]
            """u8;
        var ts = LintJson.ReadTslint(tslint);
        Check.SequenceEqual(["/w/b.ts", "/w/c.ts"], [.. ts.Select(f => f.Path)], "grouped by file in the order each first appears");
        Check.True(ts[0].Diagnostics[0] == new FileDiagnostic(1, 14, 1, 14, DiagnosticSeverity.Error, "semicolon", "Missing semicolon"), "0-based positions become lines and columns from 1");
        Check.True(ts[0].Diagnostics[1] is { Severity: DiagnosticSeverity.Warning, Line: 3 } && ts[0].Fixes.Count(f => f.Diagnostic == 1) == 2, "a fix of two replacements: two fixes of one diagnostic");
        Check.True(ts[1].Diagnostics[0].Severity == DiagnosticSeverity.Info && ts[1].Fixes.Count == 0, "another severity is information");
        Check.True(LintJson.ReadTslint(""u8).Count == 0 && LintJson.ReadTslint("\n"u8).Count == 0, "TSLint writes nothing when nothing failed");

        Check.Equal("9.39.1", LintJson.TopLevelString("﻿{ // npm's\n \"name\": \"eslint\", \"dependencies\": {\"version\": \"no\"}, \"version\": \"9.39.1\", }"u8.ToArray(), "version"u8),
            "a package.json's version: a BOM, a comment, a nested key passed over");
        Check.True(LintJson.HasTopLevel("{\"name\":\"a\",\"prettier\":{\"semi\":false}}"u8, "prettier"u8) && !LintJson.HasTopLevel("{\"x\":{\"prettier\":1}}"u8, "prettier"u8)
            && !LintJson.HasTopLevel("not json"u8, "prettier"u8), "a top-level key only");
    }

    // ---- the detection matrix ----

    [Test]
    public static void TheDetectionMatrixFindsTheProjectsConfigAndInstall()
    {
        using var temp = new TempFolder();
        string root = Path.GetFullPath(temp.Path);
        var eslint = EslintLinter.Instance;
        var tslint = TslintLinter.Instance;
        string Abs(string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        void Install(string folder, string package, string script, string version)
        {
            Write(Abs($"{folder}/node_modules/{package}/{script}"), "// tool");
            Write(Abs($"{folder}/node_modules/{package}/package.json"), $"{{\"name\":\"{package}\",\"version\":\"{version}\"}}");
        }

        Check.True(eslint.Detect(Abs("src/a.js"), root) is null && tslint.Detect(Abs("src/a.ts"), root) is null, "no configuration: neither linter applies (the session offers Set up ESLint)");

        // A flat configuration with the project's ESLint 9: ready, as it is.
        Write(Abs("flat/eslint.config.mjs"), "export default [];");
        Install("flat", "eslint", "bin/eslint.js", "9.39.1");
        var flat = eslint.Detect(Abs("flat/src/deep/a.ts"), root);
        Check.True(flat is { Ready: true, Version: "9.39.1", Note: null } && flat.Folder == Abs("flat") && flat.Config == Abs("flat/eslint.config.mjs") && flat.Environment.Count == 0,
            $"the nearest eslint.config.* up the folders, the project's own install, its version read ({flat?.Problem})");
        var command = eslint.Command("/n/node", flat!, [Abs("flat/src/deep/a.ts")], "/data/out.json");
        Check.SequenceEqual(["/n/node", flat!.Program!, "--format", "json", "--output-file", "/data/out.json", "--no-warn-ignored", "src/deep/a.ts"], command,
            "node eslint.js --format json --output-file REPORT, the file relative to the configuration's folder");
        Check.True(eslint.Command("/n/node", flat, [], "/o")[^1] == ".", "a whole project: '.'");

        // Flat first, even when a legacy file sits nearer; a legacy one alone: ESLint 9's legacy mode, said; ESLint 10 refuses it.
        Write(Abs("flat/src/.eslintrc.json"), "{}");
        Check.True(eslint.Detect(Abs("flat/src/a.js"), root) is { Config: var config } && config == Abs("flat/eslint.config.mjs"), "a flat configuration wins over a nearer .eslintrc");
        Write(Abs("legacy9/.eslintrc.json"), "{}");
        Install("legacy9", "eslint", "bin/eslint.js", "9.12.0");
        var legacy = eslint.Detect(Abs("legacy9/a.js"), root);
        Check.True(legacy is { Ready: true, Note: { } note } && legacy.Environment["ESLINT_USE_FLAT_CONFIG"] == "false" && note.Contains("ESLINT_USE_FLAT_CONFIG=false", StringComparison.Ordinal)
            && note.Contains("eslint.config.js", StringComparison.Ordinal), $"ESLint 9 runs .eslintrc with ESLINT_USE_FLAT_CONFIG=false, and says so with the way out ({legacy?.Note})");
        Write(Abs("legacy10/package.json"), "{\"name\":\"x\",\"eslintConfig\":{\"rules\":{}}}");
        Install("legacy10", "eslint", "bin/eslint.js", "10.1.0");
        Check.True(eslint.Detect(Abs("legacy10/a.js"), root) is { Ready: false, Problem: { } refused } && refused.Contains("migrate", StringComparison.Ordinal) && refused.Contains("package.json", StringComparison.Ordinal),
            "package.json's eslintConfig is a legacy configuration; ESLint 10 no longer reads one: said, with the migration");
        Write(Abs("legacy8/.eslintrc.yml"), "rules: {}");
        Install("legacy8", "eslint", "bin/eslint.js", "8.57.1");
        Check.True(eslint.Detect(Abs("legacy8/a.js"), root) is { Ready: true, Note: null, Environment.Count: 0 }, "ESLint 8 reads .eslintrc as it is");

        // A configuration without ESLint installed: why; a global one only when allowed.
        Write(Abs("bare/eslint.config.js"), "export default [];");
        var bare = eslint.Detect(Abs("bare/a.js"), root);
        Check.True(bare is { Ready: false, Problem: { } why } && why.Contains("npm install", StringComparison.Ordinal) && why.Contains("CRAFT_PLUGIN_LINT_ALLOWGLOBAL", StringComparison.Ordinal),
            $"no install: said, naming npm install and the global setting ({bare?.Problem})");
        string node = Abs($"global/bin/{NodeName}");
        Write(node, "not a program");
        Write(Abs("global/lib/node_modules/eslint/bin/eslint.js"), "// global");
        Write(Abs("global/lib/node_modules/eslint/package.json"), "{\"version\":\"9.30.0\"}");
        LintNode.Set(node, null);
        try
        {
            eslint.AllowGlobal = true;
            Check.True(eslint.Detect(Abs("bare/a.js"), root) is { Ready: true, Version: "9.30.0", Note: { } global } && global.Contains("global", StringComparison.Ordinal),
                "allowed (CRAFT_PLUGIN_LINT_ALLOWGLOBAL), the global ESLint beside Node runs, said");
        }
        finally
        {
            eslint.AllowGlobal = false;
            LintNode.Set(null, "reset by the test");
        }

        // TSLint: only tslint.json; deprecated, said; its command with the project's tsconfig.json.
        Write(Abs("old/tslint.json"), "{}");
        Write(Abs("old/tsconfig.json"), "{}");
        Install("old", "tslint", "bin/tslint", "6.1.3");
        Check.True(eslint.Detect(Abs("old/src/b.ts"), root) is null, "no ESLint configuration in the TSLint project");
        var old = tslint.Detect(Abs("old/src/b.ts"), root);
        Check.True(old is { Ready: true, Version: "6.1.3", Note: { } deprecated } && deprecated.Contains("deprecated", StringComparison.Ordinal), "TSLint with tslint.json, said deprecated");
        Check.SequenceEqual(["/n/node", old!.Program!, "--format", "json", "--out", "/o.json", "--project", "tsconfig.json", "src/b.ts"], tslint.Command("/n/node", old, [Abs("old/src/b.ts")], "/o.json"),
            "node tslint --format json --out REPORT --project tsconfig.json FILES");

        // Never above the workspace's root; the registry by extension.
        Write(Path.Combine(Path.GetDirectoryName(root)!, $"outside-{Path.GetFileName(root)}", "eslint.config.js"), "");
        Check.True(LintProjects.Upward(Abs("x/y/a.js"), root).Last() == root && LintProjects.Upward(Abs("x/y/a.js"), root).Count() == 3, "the walk stops at the root");
        Check.SequenceEqual(["bare", "flat", "flat/src", "legacy8", "legacy9", "old"],
            [.. LintProjects.FoldersWith(root, [.. LintProjects.EslintFlat, .. LintProjects.EslintLegacy, "tslint.json"], ["node_modules"]).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))],
            "Lint workspace's projects: each folder with a configuration file, node_modules never entered (package.json's eslintConfig is read only up a file's folders)");
        Linters.Register(eslint);
        Linters.Register(tslint);
        try
        {
            Check.SequenceEqual(["eslint", "tslint"], [.. Linters.ForFile("a.TS").Select(l => l.Name)], "both lint .ts (ESLint asked first), by extension ignoring case");
            Check.True(Linters.ForFile("a.css").Count == 0 && Linters.Find("ESLINT") == eslint, "none for .css; names ignore case");
            Check.Throws<NotRegisteredException>(() => Linters.Get("stylelint"), "an unregistered linter is \"not registered\"");
        }
        finally
        {
            Linters.Unregister("eslint");
            Linters.Unregister("tslint");
        }

        var settings = new LintSettings();
        Check.True(settings.Picks("eslint") && settings.Picks("tslint") && !(settings with { Linter = "tslint" }).Picks("eslint") && !(settings with { Linter = "off" }).Picks("eslint"),
            "linter = auto | eslint | tslint | off");
    }

    // ---- published diagnostics: merged per source, Problems for closed files ----

    [Test]
    public static void PublishedDiagnosticsMergePerSourceAndProblemsListsClosedFiles()
    {
        using var temp = new TempFolder();
        string path = Path.GetFullPath(temp.File("app.fake"));
        Write(path, "const x = 1\nlet y = 2\nfoo();\n");
        using var host = new EditorHost(new CraftOptions());
        var language = new FakeSemanticLanguage();
        var document = TextDocument.OpenAsync(path).GetAwaiter().GetResult();
        var open = host.Open(document, language);
        using var model = new EditorModel(document, language, new CraftOptions { InlineDiagnostics = "all" }, EditorThemes.Get(EditorThemes.Dark), new FixedMeasurer(), new FakeClipboard());
        model.UpdateViewport(0, 0, 800, 400);
        var ui = new ConcurrentQueue<Action>();
        model.Dispatch = ui.Enqueue;
        var snapshot = document.Buffer.Current;
        language.Last!.Diagnostics = new DiagnosticSet(snapshot, [new EditorDiagnostic(snapshot.GetLine(2).Start, 3, DiagnosticSeverity.Error, "CS0103", "foo is not defined")]);
        language.Last.Raise();
        LayerUi.Pump(ui);

        Check.True(host.PublishDiagnostics(path, "eslint", snapshot,
        [
            new FileDiagnostic(1, 7, 1, 8, DiagnosticSeverity.Error, "no-unused-vars", "'x' is assigned a value but never used."),
            new FileDiagnostic(1, 12, 2, 1, DiagnosticSeverity.Warning, "semi", "Missing semicolon."),
        ]), "the host shows published diagnostics");
        var all = model.Diagnostics.ToArray();
        Check.True(all.Length == 3 && all.Select(d => d.Start).SequenceEqual(all.Select(d => d.Start).Order()), $"merged with the language's own, in position order ({all.Length})");
        Check.True(all[0] is { Start: 6, Length: 1, Id: "eslint(no-unused-vars)", Source: "eslint" } && all[1] is { Start: 11, Length: 1, Id: "eslint(semi)" } && all[2] is { Id: "CS0103", Source: null },
            $"each id read source(id), its source kept; the language's untouched ({string.Join(", ", all.Select(d => d.Id))})");
        Check.True(model.ErrorCount == 2 && model.WarningCount == 1, "the counts take both");
        Check.True(model.DiagnosticsAt(6).Single().Id == "eslint(no-unused-vars)", "the hover's problem text names its source");

        host.PublishDiagnostics(path, "tslint", null, [FileDiagnostic.At(2, 1, DiagnosticSeverity.Info, "no-var-keyword", "use let")]);
        host.PublishDiagnostics(path, "eslint", snapshot, [new FileDiagnostic(1, 12, 2, 1, DiagnosticSeverity.Warning, "semi", "Missing semicolon.")]);
        Check.SequenceEqual(["eslint(semi)", "tslint(no-var-keyword)", "CS0103"], [.. model.Diagnostics.ToArray().Select(d => d.Id)], "a source's second list replaces only its own; another source joins");

        // Edits move them; an edit on one drops it; a list published for an older text is moved through the edits since.
        var before = document.Buffer.Current;
        document.Edit([new TextEdit(0, 0, "// a\n")], EditKind.Command, SelectionState.At(0), SelectionState.At(5));
        Check.True(model.Diagnostics.ToArray().Single(d => d.Id == "eslint(semi)").Start == 16, "an edit before moves the published ones");
        host.PublishDiagnostics(path, "eslint", before, [new FileDiagnostic(1, 7, 1, 8, DiagnosticSeverity.Error, "no-unused-vars", "unused")]);
        Check.True(model.Diagnostics.ToArray().Single(d => d.Source == "eslint").Start == 11, "published for the text before the edit: moved to where it is now");
        document.Edit([new TextEdit(11, 1, "z")], EditKind.Command, SelectionState.At(11), SelectionState.At(12));
        Check.True(!model.Diagnostics.ToArray().Any(d => d.Source == "eslint"), "an edit on its text drops it, as the language's");

        // The line-end message says whose it is.
        host.PublishDiagnostics(path, "eslint", null, [FileDiagnostic.At(2, 1, DiagnosticSeverity.Error, "semi", "Missing semicolon.")]);
        var canvas = new RecordingCanvas();
        using (var renderer = new EditorRenderer())
        {
            renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        }

        Check.True(canvas.Texts.Any(t => t.Text == "Missing semicolon.  eslint(semi)"), $"the inline message carries its source ({string.Join(" | ", canvas.Texts.Where(t => t.Text.Contains("semi", StringComparison.Ordinal)).Select(t => t.Text))})");

        // A closed file: by line and column in Problems, no text read; opened, onto its text; closed, back.
        string other = Path.GetFullPath(temp.File("lib/util.ts"));
        Write(other, "export const a = 1\n\nexport var b = 2\n");
        int changes = 0;
        host.PublishedDiagnosticsChanged += (_, _) => changes++;
        host.PublishDiagnostics(other, "eslint", null, [FileDiagnostic.At(3, 8, DiagnosticSeverity.Warning, "no-var", "Unexpected var."), FileDiagnostic.At(1, 19, DiagnosticSeverity.Error, "semi", "Missing semicolon.")]);
        var closed = host.ClosedProblemFiles.Single();
        Check.True(closed.Path == other && closed.Diagnostics.Count == 2 && changes == 1, "the closed file's diagnostics are kept by line and column, Problems told");
        var list = new ProblemsList();
        list.Update([ProblemSource.ForClosedFile(closed.Path, closed.Diagnostics, closed)], 100, false);
        Check.True(list.Groups.Single() is { Title: "util.ts", Header: "util.ts  2" } group && ReferenceEquals(group.Owner, closed), "Problems lists the closed file under its name");
        Check.True(list.Items[0] is { Line: 1, Column: 19, Text: "eslint(semi)  Missing semicolon.", Position: -1 } && list.Items[1] is { Line: 3, Column: 8 } && list.Items[0].CopyText.StartsWith("util.ts(1,19): error eslint(semi)", StringComparison.Ordinal),
            $"errors first, by line and column, the source in each row ({list.Items[0].Text}; {list.Items[0].CopyText})");
        var otherDocument = TextDocument.OpenAsync(other).GetAwaiter().GetResult();
        var otherOpen = host.Open(otherDocument, null);
        Check.True(host.ClosedProblemFiles.Count == 0 && DocumentDiagnostics.Of(otherDocument).All.ToArray() is [{ Id: "eslint(semi)", Start: 18 }, { Id: "eslint(no-var)", Start: 27 }],
            "opened: its diagnostics go onto its text");
        host.Close(otherOpen);
        Check.True(host.ClosedProblemFiles.Single().Diagnostics.Select(d => (d.Line, d.Column)).SequenceEqual([(1, 19), (3, 8)]), "closed: back by line and column");
        host.PublishDiagnostics(other, "eslint", null, []);
        Check.True(host.ClosedProblemFiles.Count == 0, "an empty list takes them away");

        host.ClearDiagnostics("eslint");
        host.ClearDiagnostics("tslint");
        Check.SequenceEqual(["CS0103"], [.. model.Diagnostics.ToArray().Select(d => d.Id)], "ClearDiagnostics takes a source's away; the language's stay");
        Check.True(new ProblemSource("k", "t", snapshot, []).Lines.Count == 0, "an open file's source keeps its snapshot form");
        _ = open;
    }

    [Test]
    public static void APluginsDiagnosticsShowWithoutALanguageAndGoWithThePlugin()
    {
        using var temp = new TempFolder();
        string path = Path.GetFullPath(temp.File("plain.js"));
        Write(path, "let a\n");
        using var host = new EditorHost(new CraftOptions());
        var document = TextDocument.OpenAsync(path).GetAwaiter().GetResult();
        host.Open(document, null);
        using var model = new EditorModel(document, null, new CraftOptions(), EditorThemes.Get(EditorThemes.Dark), new FixedMeasurer(), new FakeClipboard());
        var plugin = new PublishingPlugin(path);
        EditorPlugins.Register(plugin);
        try
        {
            host.RefreshPlugins();
            Check.True(model.SemanticSession is null && model.Diagnostics.ToArray() is [{ Id: "probe(rule)", Source: "probe" }], "a file with no language's diagnostics shows a plug-in's");
            Check.True(model.CanFormat == DocumentFormatters.Handles(path, model.LanguageName), "Format Document is offered only where a formatter applies");
            host.Deactivate(plugin.Name);
            Check.True(model.Diagnostics.Length == 0, "deactivated, the plug-in's diagnostics leave every file");
        }
        finally
        {
            EditorPlugins.Unregister(plugin.Name);
        }
    }

    // ---- the lint plug-in: a fix as one undo step ----

    [Test]
    public static void AFixIsOneDiffTheUserAcceptsAndOneUndoStep()
    {
        using var lint = new Fixture(EslintProject);
        lint.Ready();
        var (open, document) = lint.Open("src/a.js");
        lint.Auto.Report = EslintReport;
        lint.Presenter.Answers.Enqueue(0);                                         // Allow in this workspace
        lint.Save(open);
        lint.Until(() => lint.Session.Reports.Count == 1, "the save's lint ran");
        var report = lint.Session.Reports.Single();
        Check.True(report is { Linter: "eslint", Problems: 3, Why: null, Cancelled: false }, $"one run of ESLint ({report.Why})");
        var (command, folder, limits) = lint.Auto.Ran.Single();
        Check.True(command[1].EndsWith(Path.Combine("eslint", "bin", "eslint.js"), StringComparison.Ordinal) && command.Contains("--output-file") && command[^1] == "src/a.js" && folder == lint.Root,
            $"node eslint.js --format json --output-file … src/a.js, in the configuration's folder ({string.Join(' ', command)})");
        Check.True(limits is { Requester: LintGate.Requester, Root: var root } && root == lint.Root && limits.Purpose!.Contains("ESLint lints a.js", StringComparison.Ordinal),
            "through the sandbox, the lint plug-in asking, what it runs said");
        Check.True(command[command.ToList().IndexOf("--output-file") + 1].StartsWith(Path.Combine(lint.DataFolder, "lint"), StringComparison.Ordinal), "the report is written in the user's data folder, never the workspace");

        using var model = lint.Model(document);
        var diagnostics = model.Diagnostics.ToArray();
        Check.True(diagnostics.Length == 3 && diagnostics.All(d => d.Source == "eslint"), "the problems on the editor's text");
        var semi = diagnostics.First(d => d.Id == "eslint(semi)");
        var actions = CodeActionProviders.ActionsFor(new CodeActionContext(open, semi.Start, semi.End, [semi]));
        Check.True(actions.Any(a => a.Title == "Fix: Missing semicolon. (semi)" && a.Provider == "lint") && actions.Any(a => a.Title == "Fix ESLint problems in file"),
            $"the lightbulb's actions: this fix and every fix of the file ({string.Join(" | ", actions.Select(a => a.Title))})");
        var unused = diagnostics.First(d => d.Id == "eslint(no-unused-vars)");
        Check.True(CodeActionProviders.ActionsFor(new CodeActionContext(open, unused.Start, unused.End, [unused])).Any(a => a.Title == "Remove unused variable 'x'."), "a suggestion offered on its own");
        Check.True(EditorCommands.Find(LintPlugin.FixFileCommand)!.CanExecute(lint.Host), "\"Lint: Fix ESLint problems in file\" is ready");

        string original = Text(document);
        lint.Presenter.Accept = true;
        lint.Wait(actions.Single(a => a.Title == "Fix ESLint problems in file").RunAsync(CancellationToken.None));
        Check.Equal("const x = 1;\nlet y = 2;\n", Text(document), "both semicolons, from fix.range, in one change");
        Check.True(lint.Presenter.Proposals.Count >= 1 && lint.Presenter.Proposals[0].Title.Contains("Fix ESLint problems in file", StringComparison.Ordinal) && lint.Presenter.Proposals[0].Edits.Count == 2,
            "proposed as one diff of two hunks the user accepts");
        document.Undo();
        Check.Equal(original, Text(document), "one undo step takes every fix back");
        Check.True(CodeActionProviders.ActionsFor(new CodeActionContext(open, 0, 0, [])).Count == 0, "the text is no longer the one linted: no fix is offered until it is linted again");
    }

    // ---- saves joined, a newer run cancels the older ----

    [Test]
    public static void SavesWithinTheDelayAreJoinedAndANewerRunCancelsTheOlder()
    {
        using var lint = new Fixture(root =>
        {
            EslintProject(root);
            Write(Path.Combine(root, "src", "b.js"), "let b = 1\n");
            Write(Path.Combine(root, "src", "c.ts"), "let c = 1\n");
        }, delayMs: 300);
        lint.Ready();
        lint.Session.Gate.RememberAsync(lint.Root, "eslint").GetAwaiter().GetResult();
        var (a, _) = lint.Open("src/a.js");
        var (b, _) = lint.Open("src/b.js");
        var (c, _) = lint.Open("src/c.ts");
        lint.Save(a);
        lint.Save(b);
        lint.Until(() => lint.Session.Reports.Count == 1, "one run for both saves", 10);
        Check.True(lint.Auto.Ran.Count == 1 && lint.Auto.Ran[0].Command.TakeLast(2).SequenceEqual(["src/a.js", "src/b.js"]), $"two saves within delayMs: one run of both files ({string.Join(' ', lint.Auto.Ran[0].Command)})");
        Check.True(lint.Presenter.Questions.Count == 0, "allowed before: no question");

        lint.Auto.Hold = true;
        lint.Save(a);
        lint.Session.StartSaved();
        lint.Until(() => lint.Auto.Ran.Count == 2, "the second run started");
        lint.Save(c);
        lint.Session.StartSaved();                                                  // the batch's end, without the pause
        lint.Until(() => lint.Session.Reports.Count == 3, "both ended");
        Check.True(lint.Session.Reports[1] is { Cancelled: true } && lint.Auto.Cancelled == 1, "the older run was cancelled by the newer");
        Check.True(lint.Auto.Ran[2].Command.TakeLast(2).SequenceEqual(["src/a.js", "src/c.ts"]) && lint.Session.Reports[2] is { Cancelled: false, Files.Count: 2 },
            "the newer run took the older's file with its own");
        Check.True(lint.Host.Activities.Recent.Any(r => r.Title.StartsWith("ESLint:", StringComparison.Ordinal)) || lint.Host.ActivityCenter.Running.Count == 0, "each run was an activity");
    }

    // ---- the once-per-workspace approval ----

    [Test]
    public static void TheApprovalIsAskedOncePerWorkspaceRememberedInTheUsersSettingsAndRevoked()
    {
        using var lint = new Fixture(EslintProject);
        var written = new Dictionary<string, string?>();
        lint.Host.SettingWriter = (variable, value) =>
        {
            written[variable] = value;
            if (value is null)
            {
                lint.Vars.Remove(variable);
            }
            else
            {
                lint.Vars[variable] = value;
            }

            return Task.FromResult<string?>(null);
        };
        lint.Ready();
        var (open, _) = lint.Open("src/a.js");

        lint.Presenter.Answers.Enqueue(1);                                         // Not now
        lint.Save(open);
        lint.Until(() => lint.Session.Reports.Count == 1, "the first save's run was decided");
        var question = lint.Presenter.Questions.Single();
        Check.True(question.Title == "Let ESLint run on its own in this workspace?" && question.Choices.SequenceEqual(["Allow in this workspace", "Not now"])
            && question.Details.Any(d => d.Contains("CRAFT_PLUGIN_LINT_ALLOWED", StringComparison.Ordinal) && d.Contains("never the workspace", StringComparison.Ordinal)),
            $"asked once, saying where the answer is kept ({question.Title}; {string.Join(" | ", question.Details)})");
        Check.True(lint.Auto.Ran.Count == 0 && lint.Session.Reports[0].Why!.Contains("not allowed", StringComparison.Ordinal), "Not now: nothing ran");
        lint.Save(open);
        lint.Until(() => lint.Session.Reports.Count == 2, "the second save");
        Check.True(lint.Presenter.Questions.Count == 1 && lint.Auto.Ran.Count == 0, "not asked again this session");

        // "Lint this file" goes through the asking sandbox, not the automatic one.
        lint.Wait(lint.Session.LintActiveAsync());
        Check.True(lint.Manual.Ran.Count == 1 && lint.Auto.Ran.Count == 0 && lint.Manual.Ran[0].Limits.Requester == LintGate.Requester, "a run the user asked for: through CRAFT_PLUGIN_LINT_SANDBOX");

        // Forget, then Allow: remembered in the user's settings (this machine's only).
        lint.Wait(lint.Session.Gate.ForgetAsync(lint.Root));
        lint.Presenter.Answers.Enqueue(0);
        lint.Save(open);
        lint.Until(() => lint.Auto.Ran.Count == 1, "allowed: the automatic run");
        string key = LintGate.Key(lint.Root, "eslint");
        Check.Equal(key, written["CRAFT_PLUGIN_LINT_ALLOWED"], "the approval written to the user's settings as folder|program");
        Check.True(EditorPlugins.IsMachineOnly("CRAFT_PLUGIN_LINT_ALLOWED") && EditorPlugins.IsMachineOnly("CRAFT_PLUGIN_LINT_SANDBOX") && EditorPlugins.IsMachineOnly("CRAFT_PLUGIN_LINT_AUTOSANDBOX")
            && !EditorPlugins.IsMachineOnly("CRAFT_PLUGIN_LINT_RUNON"), "the approvals and the sandboxes are this machine's only: a workspace cannot allow itself");
        using (var settingsFolder = new TempFolder())
        {
            var user = SettingsFile.Load(settingsFolder.File("user/settings.json"));
            var workspace = SettingsFile.Load(settingsFolder.File("ws/.craft/settings.json"));
            workspace.Set("CRAFT_PLUGIN_LINT_ALLOWED", lint.Root + "|eslint");
            Check.True(string.IsNullOrEmpty(new CraftSettings(_ => null, user, workspace).Read("CRAFT_PLUGIN_LINT_ALLOWED")), "a workspace's own approval is passed over");
        }

        lint.Presenter.Questions.Clear();
        lint.Save(open);
        lint.Until(() => lint.Auto.Ran.Count == 2, "the next save runs at once");
        Check.True(lint.Presenter.Questions.Count == 0, "remembered: no question");

        // Revoked on the settings page (the row removed): asked again.
        lint.Vars.Remove("CRAFT_PLUGIN_LINT_ALLOWED");
        Check.True(lint.Wait(lint.Session.Gate.ForgetAsync(lint.Root)) >= 1 && !lint.Session.Gate.IsAllowed(lint.Root, "eslint"), "taken back");
        lint.Presenter.Answers.Enqueue(1);
        lint.Save(open);
        lint.Until(() => lint.Presenter.Questions.Count == 1, "asked again");
        Check.True(EditorCommands.Find(LintPlugin.ForgetCommand) is not null, "\"Lint: Forget this workspace's approvals\" in the palette");
    }

    // ---- no ESLint: Set up ESLint; TSLint said deprecated once ----

    [Test]
    public static void AProjectWithoutEslintIsOfferedSetUpAndTslintIsSaidDeprecatedOnce()
    {
        using var lint = new Fixture(root =>
        {
            Write(Path.Combine(root, "package.json"), "{\"name\":\"web\"}");
            Write(Path.Combine(root, "src", "a.js"), "let a = 1\n");
            Write(Path.Combine(root, "old", "tslint.json"), "{}");
            Write(Path.Combine(root, "old", "node_modules", "tslint", "bin", "tslint"), "// tslint");
            Write(Path.Combine(root, "old", "node_modules", "tslint", "package.json"), "{\"version\":\"6.1.3\"}");
            Write(Path.Combine(root, "old", "b.ts"), "var b = 1\n");
        });
        lint.Ready();
        lint.Session.Gate.RememberAsync(lint.Root, "tslint").GetAwaiter().GetResult();
        var (a, _) = lint.Open("src/a.js");
        lint.Save(a);
        lint.Session.StartSaved();
        lint.Until(() => lint.Host.Notifications.Entries.Any(e => e.Title == "Lint" && e.Actions.Any(x => x.Label == "Set up ESLint")), "Set up ESLint offered");
        Check.True(lint.Auto.Ran.Count == 0, "nothing ran without a configuration");

        lint.Auto.Report = _ => "[]";
        var (b, _) = lint.Open("old/b.ts");
        lint.Save(b);
        lint.Session.StartSaved();
        lint.Until(() => lint.Session.Reports.Count == 1, "TSLint ran");
        lint.Save(b);
        lint.Session.StartSaved();
        lint.Until(() => lint.Session.Reports.Count == 2, "again");
        Check.True(lint.Auto.Ran[0].Command.Contains("--out") && lint.Auto.Ran[0].Command[1].EndsWith(Path.Combine("tslint", "bin", "tslint"), StringComparison.Ordinal), "the project's TSLint");
        Check.Equal(1, lint.Host.Notifications.Entries.Count(e => e.Text.Contains("deprecated", StringComparison.Ordinal)), "said deprecated once per workspace");

        // The setup runs npm init @eslint/config@latest through the asking sandbox, in a terminal tab.
        lint.Wait(lint.Session.SetupAsync());
        var started = lint.Manual.Started.Single();
        Check.True(started.Command.Skip(2).SequenceEqual(["init", "@eslint/config@latest"]) && started.Command[1].EndsWith("npm-cli.js", StringComparison.Ordinal) && started.Folder == lint.Root
            && started.Limits.Requester == LintGate.Requester && started.Limits.Time == Timeout.InfiniteTimeSpan, "node npm-cli.js init @eslint/config@latest, through the sandbox, no time limit (it asks questions)");
    }

    // ---- Prettier: chosen only with a configuration ----

    [Test]
    public static void PrettierIsChosenOnlyWhereTheProjectHasAConfig()
    {
        using var lint = new Fixture(root =>
        {
            Write(Path.Combine(root, "package.json"), "{\"name\":\"web\"}");
            Write(Path.Combine(root, "node_modules", "prettier", "bin", "prettier.cjs"), "// prettier");
            Write(Path.Combine(root, "node_modules", "prettier", "package.json"), "{\"name\":\"prettier\",\"version\":\"3.8.1\"}");
            Write(Path.Combine(root, "src", "a.ts"), "const  a =  1;\n");
            Write(Path.Combine(root, "pkg", "package.json"), "{\"name\":\"pkg\",\"prettier\":{\"semi\":false}}");
            Write(Path.Combine(root, "pkg", "b.css"), "a {  color: red }\n");
        });
        lint.Ready();
        var formatter = PrettierFormatter.Instance;
        Check.True(DocumentFormatters.Find("prettier") == formatter && formatter.Formats("x.TSX", "") && formatter.Formats("x.yml", "") && !formatter.Formats("x.cs", "csharp"),
            "registered; JS, TS, JSX, TSX, CSS, SCSS, LESS, HTML, JSON, Markdown and YAML by extension");
        string a = Path.Combine(lint.Root, "src", "a.ts");
        var none = lint.Wait(formatter.FitAsync(a, lint.Root));
        Check.True(!none.Applies && none.Description.Contains("no Prettier configuration", StringComparison.Ordinal), $"installed, no configuration: Craft's own formatter ({none.Description})");
        Check.True(PrettierFormatter.Fit(Path.Combine(lint.Root, "src"), lint.Root, new PrettierSettings { RequireConfig = false }).Applies, "with requireConfig off, the install is enough");
        var pkg = lint.Wait(formatter.FitAsync(Path.Combine(lint.Root, "pkg", "b.css"), lint.Root));
        Check.True(pkg is { Applies: true, Description: "Prettier 3.8.1, the project's (package.json)" }, $"\"prettier\" in package.json is a configuration ({pkg.Description})");
        Write(Path.Combine(lint.Root, ".prettierrc"), "{\"semi\": false}");
        Check.True(!lint.Wait(formatter.FitAsync(a, lint.Root)).Applies, "what a folder holds is read once (rule 75)");
        formatter.Forget();
        var fit = lint.Wait(formatter.FitAsync(a, lint.Root));
        Check.True(fit is { Applies: true, Description: "Prettier 3.8.1, the project's (.prettierrc)" }, $"a .prettierrc: Prettier formats here ({fit.Description})");
        Check.True(lint.Wait(DocumentFormatters.ChooseAsync(a, "typescript", lint.Root)).Formatter == formatter && DocumentFormatters.Describe(a, "typescript")!.Description == fit.Description,
            "the registry chooses it, and describes it without reading");
        Check.True(lint.Wait(DocumentFormatters.ChooseAsync(Path.Combine(lint.Root, "x.cs"), "csharp", lint.Root)).Formatter is null, "a language it does not format keeps its own");

        // Format Document through the editor: the project's Prettier, one undo step, the tooltip naming it.
        var (open, document) = lint.Open("src/a.ts");
        using var model = lint.Model(document);
        Check.True(model.CanFormat && model.FormattingSession is null, "a .ts with no formatter of its language can still be formatted (Prettier)");
        Check.Equal("Formatter: Prettier 3.8.1, the project's (.prettierrc)", ShellText.FormatterTooltip(model), "said on the status bar's language tooltip");
        lint.Auto.Format = text => text.Replace("  ", " ", StringComparison.Ordinal);
        lint.Session.Gate.RememberAsync(lint.Root, "prettier").GetAwaiter().GetResult();
        Check.True(lint.Wait(model.FormatDocumentAsync()), "formatted");
        Check.Equal("const a = 1;\n", Text(document), "the text Prettier gave");
        var (command, folder, limits) = lint.Auto.Ran.Single();
        Check.True(command[1].EndsWith("craft-prettier.cjs", StringComparison.Ordinal) && command[2] == Path.Combine(lint.Root, "node_modules", "prettier") && command[4] == Path.GetFullPath(a)
            && command[1].StartsWith(lint.DataFolder, StringComparison.Ordinal) && limits.Requester == LintGate.Requester,
            $"Craft's runner in the data folder, the project's Prettier, the file's path for its configuration ({string.Join(' ', command)})");
        Check.True(File.ReadAllText(command[1]) == PrettierFormatter.Script, "the runner script written once, as shipped");
        document.Undo();
        Check.Equal("const  a =  1;\n", Text(document), "one undo step");

        // On save without an approval: asked; Not now leaves the file as it is.
        lint.Wait(lint.Session.Gate.ForgetAsync(lint.Root));
        lint.Presenter.Answers.Enqueue(1);
        Check.True(!lint.Wait(model.FormatDocumentAsync(automatic: true)) && Text(document) == "const  a =  1;\n" && lint.Presenter.Questions.Any(q => q.Title == "Let Prettier run on its own in this workspace?"),
            "format on save asks once; declined, nothing changes");
        _ = open;
    }

    // ---- the plug-in from its own folder ----

    [Test]
    public static void TheLintPluginLoadsFromItsFolderAndCanBeRemoved()
    {
        Check.True(EditorPlugins.Find(LintSettings.PluginName) is null && EditorPlugins.Find(PrettierSettings.PluginName) is null, "before it loads, neither is registered");
        using var temp = new TempFolder();
        var output = PhasePTests.PluginOutput(LintPluginPackage.PackageName);
        var folder = Path.Combine(temp.Path, "plugins", LintPluginPackage.PackageName);
        foreach (var file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(folder, Path.GetRelativePath(output, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        Check.True(!File.Exists(Path.Combine(folder, "Craft.Abstractions.dll")) && !File.Exists(Path.Combine(folder, "Idrak.Abstraction.dll")) && File.Exists(Path.Combine(folder, "Craft.Plugins.Npm.dll")),
            "it carries no copy of the shared contracts, and its own copy of the npm plug-in's Node finder");
        var result = CraftPlugins.LoadFolder(folder);
        try
        {
            Check.True(result is { Loaded: true, Manifest.ContractVersion: "1.1" }, $"it loads from its folder ({result.Error})");
            var plugin = EditorPlugins.Get(LintSettings.PluginName);
            Check.True(AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly) != AssemblyLoadContext.Default, "\"lint\" comes from the plug-in's own load context");
            Check.True(plugin.Settings.All(s => s.Shape?.Section == SettingSections.Editor) && plugin.Settings.Count(s => s.MachineOnly) == 4, "its settings under Editor › Lint, four machine-only");
            Check.True(EditorPlugins.Get(PrettierSettings.PluginName).Settings.Count(s => s.MachineOnly) == 1, "\"prettier\" with its own settings (CRAFT_PLUGIN_PRETTIER_*)");
        }
        finally
        {
            var loaded = AssemblyLoadContext.All.Where(c => c != AssemblyLoadContext.Default).SelectMany(c => c.Assemblies)
                .LastOrDefault(x => x.GetName().Name == LintPluginPackage.PackageName && x.Location.StartsWith(folder, StringComparison.Ordinal));
            if (loaded is not null && IdrakPlugins.PackageOf(loaded) is { } package)
            {
                IdrakPlugins.UnregisterAll(package);
            }
        }

        Check.True(EditorPlugins.Find(LintSettings.PluginName) is null && EditorPlugins.Find(PrettierSettings.PluginName) is null, "removed: both are gone");
    }

    // ---- real ESLint and Prettier (skipped without them) ----

    [Test]
    public static void RealEslintLintsAProjectAndItsFixIsAppliedOrTheTestSaysWhyItSkipped()
    {
        var node = NodeInstall.Find(new NpmSettings(), Environment.GetEnvironmentVariable, OperatingSystem.IsWindows());
        string? Tool(string name) =>
            Environment.GetEnvironmentVariable("CRAFT_TEST_" + name.ToUpperInvariant()) is { Length: > 0 } given && Directory.Exists(given) ? given
            : node.Node is { } program && Path.GetDirectoryName(program) is { } bin
                ? new[] { Path.Combine(bin, "node_modules", name), Path.Combine(bin, "..", "lib", "node_modules", name) }.Select(Path.GetFullPath).FirstOrDefault(Directory.Exists)
                : null;
        if (node.Node is null || Tool("eslint") is not { } eslint)
        {
            Console.WriteLine($"  skipped: the project's ESLint is missing (no Node, or no ESLint to link into the test project: set CRAFT_TEST_ESLINT to an eslint package folder) ({node.Problem})");
            return;
        }

        if (CommandSandboxes.Find(CommandSandboxes.Process) is null)
        {
            CommandSandboxes.Register(new Idrak.Text.Generation.ProcessCommandSandbox());
        }

        string? prettier = Tool("prettier");
        using var lint = new Fixture(root =>
        {
            Write(Path.Combine(root, "package.json"), "{\"name\":\"real\",\"type\":\"module\"}");
            Write(Path.Combine(root, "eslint.config.mjs"), "export default [{ rules: { \"no-unused-vars\": \"error\", \"semi\": [\"warn\", \"always\"] } }];\n");
            Write(Path.Combine(root, "src", "a.js"), "let é = \"ü\"; console.log(é)\nconst x = 1;\n");
            Write(Path.Combine(root, "src", "clean.js"), "console.log(1);\n");
        }, node: node.Node, vars: new() { ["CRAFT_PLUGIN_LINT_SANDBOX"] = CommandSandboxes.Process, ["CRAFT_PLUGIN_LINT_AUTOSANDBOX"] = CommandSandboxes.Process });
        try
        {
            Directory.CreateDirectory(Path.Combine(lint.Root, "node_modules"));
            Directory.CreateSymbolicLink(Path.Combine(lint.Root, "node_modules", "eslint"), eslint);
            if (prettier is not null)
            {
                Directory.CreateSymbolicLink(Path.Combine(lint.Root, "node_modules", "prettier"), prettier);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"  skipped: ESLint could not be linked into the test project ({e.Message})");
            return;
        }

        lint.Ready();
        var clock = Stopwatch.StartNew();
        lint.Wait(lint.Session.LintWorkspaceAsync(), 120);
        Check.True(lint.Session.Reports.Single() is { Why: null, Problems: 2 }, $"Lint workspace ran the project's ESLint ({lint.Session.Reports.SingleOrDefault()?.Why})");
        Console.WriteLine($"  ESLint {LintProjects.VersionOf(Path.Combine(lint.Root, "node_modules", "eslint", "bin", "eslint.js"))}: Lint workspace in {clock.ElapsedMilliseconds} ms");
        var closed = lint.Host.ClosedProblemFiles.Single();
        Check.True(closed.Path == Path.Combine(lint.Root, "src", "a.js")
            && closed.Diagnostics.Any(d => d is { Id: "eslint(no-unused-vars)", Line: 2, Column: 7, Severity: DiagnosticSeverity.Error })
            && closed.Diagnostics.Any(d => d is { Id: "eslint(semi)", Line: 1, Column: 28, Severity: DiagnosticSeverity.Warning }),
            $"the closed file's problems in Problems, by line and column ({string.Join(", ", closed.Diagnostics.Select(d => $"{d.Id}@{d.Line}:{d.Column}"))})");

        var (open, document) = lint.Open("src/a.js");
        using var model = lint.Model(document);
        Check.True(model.Diagnostics.ToArray().Any(d => d.Id == "eslint(semi)"), "opened: on its text");
        lint.Presenter.Accept = true;
        Check.True(lint.Wait(lint.Session.FixFileAsync(), 30), "Fix ESLint problems in file");
        Check.Equal("let é = \"ü\"; console.log(é);\nconst x = 1;\n", Text(document), "ESLint's own fix, its UTF-16 position past non-ASCII characters");
        document.Undo();

        if (prettier is null)
        {
            Console.WriteLine("  Prettier part skipped: no Prettier to link (CRAFT_TEST_PRETTIER)");
            return;
        }

        Write(Path.Combine(lint.Root, ".prettierrc"), "{\"semi\": false, \"singleQuote\": true}");
        PrettierFormatter.Instance.Forget();
        lint.Presenter.Answers.Enqueue(0);
        clock.Restart();
        Check.True(lint.Wait(model.FormatDocumentAsync(automatic: true), 60), $"the real Prettier formatted ({model.LastFormatMessage})");
        Console.WriteLine($"  Prettier: Format Document in {clock.ElapsedMilliseconds} ms");
        Check.Equal("let é = 'ü'\nconsole.log(é)\nconst x = 1\n", Text(document), "the project's .prettierrc applied by Craft's runner");
        _ = open;
    }

    // ---- the fixture ----

    private static void EslintProject(string root)
    {
        Write(Path.Combine(root, "package.json"), "{\"name\":\"web\"}");
        Write(Path.Combine(root, "eslint.config.mjs"), "export default [];");
        Write(Path.Combine(root, "node_modules", "eslint", "bin", "eslint.js"), "// eslint");
        Write(Path.Combine(root, "node_modules", "eslint", "package.json"), "{\"name\":\"eslint\",\"version\":\"9.39.1\"}");
        Write(Path.Combine(root, "src", "a.js"), "const x = 1\nlet y = 2\n");
    }

    // ESLint's report for src/a.js "const x = 1\nlet y = 2\n": an unused x (with a suggestion), two missing semicolons.
    private static string EslintReport(IReadOnlyList<string> files) => "[" + string.Join(",", files.Select(f => f.EndsWith("a.js", StringComparison.Ordinal)
        ? $$$"""
            {"filePath":{{{System.Text.Json.JsonSerializer.Serialize(f)}}},"messages":[
             {"ruleId":"no-unused-vars","severity":2,"message":"'x' is assigned a value but never used.","line":1,"column":7,"endLine":1,"endColumn":8,"suggestions":[{"fix":{"range":[0,11],"text":""},"desc":"Remove unused variable 'x'."}]},
             {"ruleId":"semi","severity":1,"message":"Missing semicolon.","line":1,"column":12,"endLine":2,"endColumn":1,"fix":{"range":[11,11],"text":";"}},
             {"ruleId":"semi","severity":1,"message":"Missing semicolon.","line":2,"column":10,"endLine":3,"endColumn":1,"fix":{"range":[21,21],"text":";"}}]}
            """
        : $$$"""{"filePath":{{{System.Text.Json.JsonSerializer.Serialize(f)}}},"messages":[]}""")) + "]";

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Text(TextDocument document) => document.Buffer.Current.GetText(0, document.Buffer.Current.Length);

    private sealed class Fixture : IDisposable
    {
        private static int _next;
        private readonly TempFolder _temp = new();
        private readonly ConcurrentQueue<Action> _queue = new();
        private readonly LintPluginPackage _package = new();
        private readonly NpmPluginPackage _npm = new();
        private readonly SynchronizationContext? _before = SynchronizationContext.Current;

        public Fixture(Action<string> workspace, int delayMs = 0, string? node = null, Dictionary<string, string>? vars = null)
        {
            Root = Path.GetFullPath(Path.Combine(_temp.Path, "ws"));
            DataFolder = Path.GetFullPath(Path.Combine(_temp.Path, "data"));
            Directory.CreateDirectory(Root);
            workspace(Root);
            int id = Interlocked.Increment(ref _next);
            Auto = new LintSandbox($"weblint-auto-{id}");
            Manual = new LintSandbox($"weblint-manual-{id}");
            CommandSandboxes.Register(Auto);
            CommandSandboxes.Register(Manual);
            Vars = new Dictionary<string, string>
            {
                ["CRAFT_PLUGIN_LINT_SANDBOX"] = Manual.Name,
                ["CRAFT_PLUGIN_LINT_AUTOSANDBOX"] = Auto.Name,
                ["CRAFT_PLUGIN_LINT_DELAYMS"] = delayMs.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            foreach (var (name, value) in vars ?? [])
            {
                Vars[name] = value;
            }

            if (node is null)
            {
                string fake = Path.Combine(_temp.Path, "node", "bin", NodeName);
                Write(fake, "not a program");
                Write(Path.Combine(_temp.Path, "node", "lib", "node_modules", "npm", "bin", "npm-cli.js"), "// npm");
                Vars["CRAFT_PLUGIN_NPM_NODE"] = fake;
            }
            else
            {
                Vars["CRAFT_PLUGIN_NPM_NODE"] = node;
            }

            SynchronizationContext.SetSynchronizationContext(new QueueContext(_queue));
            IdrakPlugins.RegisterAll(_npm);
            IdrakPlugins.RegisterAll(_package);
            Host = new EditorHost(new CraftOptions { DataFolder = DataFolder }, _queue.Enqueue, Presenter, name => Vars.TryGetValue(name, out var v) ? v : null)
            {
                WorkspaceFolder = Root,
                Terminals = Terminals,
            };
            Host.RefreshPlugins();
            Pump();
        }

        public string Root { get; }

        public string DataFolder { get; }

        public Dictionary<string, string> Vars { get; }

        public EditorHost Host { get; }

        public LintSandbox Auto { get; }

        public LintSandbox Manual { get; }

        public Terminals Terminals { get; } = new();

        public Presenter Presenter { get; } = new();

#pragma warning disable CA1822 // The fixture's own view of the plug-in's active session.
        public LintSession Session => LintPlugin.Session ?? throw new InvalidOperationException("the lint plug-in did not activate");
#pragma warning restore CA1822

        public void Ready() => Until(() => Session.Ready.IsCompleted, "Node looked for", 30);

        public (IEditorDocument Open, TextDocument Document) Open(string relative)
        {
            var document = Wait(TextDocument.OpenAsync(Path.Combine(Root, relative)));
            var open = Host.Open(document, null);
            Pump();
            return (open, document);
        }

        public EditorModel Model(TextDocument document)
        {
            var model = new EditorModel(document, null, new CraftOptions { NewLine = "\n" }, EditorThemes.Get(EditorThemes.Dark), new FixedMeasurer(), new FakeClipboard(), workspaceRoot: Root);
            model.Dispatch = _queue.Enqueue;
            model.UpdateViewport(0, 0, 800, 400);
            return model;
        }

        public void Save(IEditorDocument document)
        {
            Wait(Host.SaveAsync(document));
            Pump();
        }

        public void Pump()
        {
            while (_queue.TryDequeue(out var action))
            {
                action();
            }
        }

        public void Until(Func<bool> done, string what, int seconds = 20)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                Pump();
                if (done())
                {
                    return;
                }

                Check.True(watch.Elapsed < TimeSpan.FromSeconds(seconds), $"{what} (waited {seconds} s)");
                Thread.Sleep(5);
            }
        }

        public T Wait<T>(Task<T> task, int seconds = 20)
        {
            Until(() => task.IsCompleted, "the task ends", seconds);
            return task.GetAwaiter().GetResult();
        }

        public void Wait(Task task, int seconds = 20)
        {
            Until(() => task.IsCompleted, "the task ends", seconds);
            task.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Pump();
            Host.Dispose();
            IdrakPlugins.UnregisterAll(_package);
            IdrakPlugins.UnregisterAll(_npm);
            CommandSandboxes.Unregister(Auto.Name);
            CommandSandboxes.Unregister(Manual.Name);
            SynchronizationContext.SetSynchronizationContext(_before);
            _temp.Dispose();
        }
    }

    private sealed class QueueContext(ConcurrentQueue<Action> queue) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => queue.Enqueue(() => d(state));

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public override SynchronizationContext CreateCopy() => this;
    }

    /// <summary>A sandbox that plays a linter: it writes the report the test gives to the command's report file, or Prettier's output.</summary>
    private sealed class LintSandbox(string name) : ICommandSandbox
    {
        public List<(IReadOnlyList<string> Command, string Folder, CommandLimits Limits)> Ran { get; } = [];

        public List<(IReadOnlyList<string> Command, string Folder, CommandLimits Limits)> Started { get; } = [];

        public Func<IReadOnlyList<string>, string> Report { get; set; } = _ => "[]";

        public Func<string, string> Format { get; set; } = text => text;

        public bool Hold { get; set; }

        public int Cancelled { get; private set; }

        public string Name { get; } = name;

        public string Summary => "plays a linter's report and Prettier's output";

        public string? Check(CommandLimits limits) => null;

        public string? CheckStart(CommandLimits limits) => null;

        public async ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
        {
            bool hold;
            lock (Ran)
            {
                Ran.Add((command, directory, limits));
                hold = Hold;
                Hold = false;
            }

            if (hold)
            {
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Cancelled++;
                    throw;
                }
            }

            int report = Math.Max(command.ToList().IndexOf("--output-file"), command.ToList().IndexOf("--out"));
            if (report > 0)
            {
                var files = command.Skip(report + 2).Where(a => !a.StartsWith("--", StringComparison.Ordinal) && a != "tsconfig.json")
                    .Select(a => Path.GetFullPath(Path.Combine(directory, a))).ToList();
                await File.WriteAllTextAsync(command[report + 1], Report(files), cancellationToken).ConfigureAwait(false);
            }
            else if (command.Count == 6 && command[1].EndsWith("craft-prettier.cjs", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(command[5], Format(await File.ReadAllTextAsync(command[3], cancellationToken).ConfigureAwait(false)), cancellationToken).ConfigureAwait(false);
            }

            return new CommandResult(0, false, "", TimeSpan.FromMilliseconds(3), false);
        }

        public ValueTask<CommandStart> StartAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
        {
            Started.Add((command, directory, limits));
            return ValueTask.FromResult(CommandStart.Refused("the test runs no program"));
        }
    }

    private sealed class Presenter : IEditorPresenter
    {
        public ConcurrentQueue<int> Answers { get; } = new();

        public List<EditorQuestion> Questions { get; } = [];

        public List<EditProposal> Proposals { get; } = [];

        public bool Accept { get; set; }

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken)
        {
            Proposals.Add(proposal);
            return Task.FromResult(Accept);
        }

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken)
        {
            lock (Questions)
            {
                Questions.Add(question);
            }

            return Task.FromResult(Answers.TryDequeue(out int answer) ? answer : question.Dismissed);
        }
    }

    private sealed class Terminals : IEditorTerminals
    {
        public List<IEditorTerminalProgram> Programs { get; } = [];

        public IReadOnlyList<IEditorTerminal> All => [];

        public IEditorTerminal? Active => null;

        public IEditorTerminal Open(EditorTerminalRequest request) => throw new InvalidOperationException("no shells in this test");

        public IEditorTerminalOutput? OpenOutput(string name) => null;

        public IEditorTerminalProgram? OpenProgram(EditorProgramRequest request)
        {
            var program = new Program(request.Name);
            Programs.Add(program);
            return program;
        }

        private sealed class Program(string title) : IEditorTerminalProgram
        {
            public string Title { get; } = title;

            public bool IsOpen => true;

            public Task OutputEnded => Task.CompletedTask;

            public bool Append(string text) => true;

            public void End(string? text = null, int exitCode = 0)
            {
            }

            public void Show()
            {
            }
        }
    }

    /// <summary>A plug-in that publishes one diagnostic for a file when it activates.</summary>
    private sealed class PublishingPlugin(string path) : IEditorPlugin
    {
        public string Name => "weblint-probe";

        public string Summary => "publishes a diagnostic";

        public IDisposable Activate(IEditorHost host)
        {
            host.PublishDiagnostics(path, "probe", null, [FileDiagnostic.At(1, 1, DiagnosticSeverity.Warning, "rule", "probed")]);
            return new LintPluginDeactivation();
        }

        private sealed class LintPluginDeactivation : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
