// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;
using Craft.CSharp;
using Craft.CSharp.Project;
using Craft.CSharp.Run;
using Craft.Editor;
using Craft.Editor.Outline;
using Craft.Editor.Shell;
using Craft.Tests.Editor;
using Craft.Tests.Runner;
using CSharpSnippetSet = Craft.CSharp.CSharpSnippets;

namespace Craft.Tests;

/// <summary>
/// A language for X part 9's editor tests: its semantic session gives an inlay hint "n:" before every digit after '(' or
/// ',', counts the requests and the ranges asked, and gives a symbol for every line that starts with "fn ".
/// </summary>
internal sealed class HintLanguage : ILanguage
{
    public const string LanguageName = "hint-test";

    public Session? Last { get; private set; }

    public string Name => LanguageName;

    public string Summary => "Test: inlay hints and symbols the test can count";

    public IReadOnlyList<string> Extensions { get; } = [".hint"];

    public LanguageRules Rules => LanguageRules.Plain;

    public ILanguageSession Open(ITextBuffer buffer) => Last = new Session();

    internal sealed class Session : ISemanticLanguageSession
    {
        private int _hintRequests;

        public int HintRequests => Volatile.Read(ref _hintRequests);

        public List<(int Version, int Start, int Length)> Asked { get; } = [];

        public DiagnosticSet Diagnostics => DiagnosticSet.Empty;

        public event EventHandler? DiagnosticsChanged
        {
            add { }
            remove { }
        }

        public bool IsSemanticDeferred => false;

        public SemanticTelemetry Telemetry => SemanticTelemetry.None;

        public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
        {
        }

        public void EnableSemantics()
        {
        }

        public Task<IReadOnlyList<InlayHint>> InlayHintsAsync(ITextSnapshot snapshot, int start, int length, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _hintRequests);
            lock (Asked)
            {
                Asked.Add((snapshot.Version, start, length));
            }

            var hints = new List<InlayHint>();
            for (int i = Math.Max(1, start); i < start + length && i < snapshot.Length; i++)
            {
                if (char.IsAsciiDigit(snapshot[i]) && snapshot[i - 1] is '(' or ' ' && (snapshot[i - 1] == '(' || (i >= 2 && snapshot[i - 2] == ',')))
                {
                    hints.Add(new InlayHint(i, "n:", InlayHintKind.Parameter) { PaddingRight = true });
                }
            }

            return Task.FromResult<IReadOnlyList<InlayHint>>(hints);
        }

        public Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(ITextSnapshot snapshot, CancellationToken cancellationToken)
        {
            var symbols = new List<DocumentSymbol>();
            for (int i = 0; i < snapshot.LineCount; i++)
            {
                var line = snapshot.GetLine(i);
                string text = snapshot.GetText(line.Start, line.Length);
                if (text.StartsWith("fn ", StringComparison.Ordinal))
                {
                    int end = text.IndexOf('(', StringComparison.Ordinal);
                    string name = text[3..(end < 0 ? text.Length : end)];
                    symbols.Add(new DocumentSymbol(name, ScopeKind.Method, line.Start, line.Length, line.Start + 3, name.Length, null, []));
                }
            }

            return Task.FromResult<IReadOnlyList<DocumentSymbol>>(symbols);
        }

        public Task<CompletionList?> GetCompletionsAsync(ITextSnapshot snapshot, int position, CompletionTrigger trigger, CancellationToken cancellationToken) => Task.FromResult<CompletionList?>(null);

        public Task<CompletionItem> ResolveCompletionAsync(CompletionList list, CompletionItem item, CancellationToken cancellationToken) => Task.FromResult(item);

        public Task<SignatureHelp?> GetSignatureHelpAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken) => Task.FromResult<SignatureHelp?>(null);

        public Task<HoverInfo?> GetHoverAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken) => Task.FromResult<HoverInfo?>(null);

        public Task<DefinitionLocation?> GetDefinitionAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken) => Task.FromResult<DefinitionLocation?>(null);

        public Task<IReadOnlyList<TextEdit>> FormatAsync(ITextSnapshot snapshot, int start, int length, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TextEdit>>([]);

        public void Release()
        {
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>A code lens provider for <see cref="HintLanguage"/>: "refs" (resolved to "2 refs") and "Run" on each symbol, clicks counted.</summary>
internal sealed class HintLensProvider : ICodeLensProvider
{
    private int _resolved;

    public string Name => "hint-test-lens";

    public string Summary => "Test: lenses the test counts";

    public IReadOnlyList<string> Languages { get; } = [HintLanguage.LanguageName];

    public int Runs { get; set; }

    public int Resolved => Volatile.Read(ref _resolved);

    public int Requests { get; private set; }

    public Task<IReadOnlyList<CodeLens>> GetLensesAsync(CodeLensRequest request, CancellationToken cancellationToken)
    {
        Requests++;
        var lenses = new List<CodeLens>();
        foreach (var symbol in request.Members)
        {
            lenses.Add(new CodeLens(symbol.Start, "… refs")
            {
                ResolveAsync = _ =>
                {
                    Interlocked.Increment(ref _resolved);
                    return Task.FromResult(new CodeLens(symbol.Start, "2 refs"));
                },
            });
            lenses.Add(new CodeLens(symbol.Start, "Run") { Order = 1, RunAsync = _ => { Runs++; return Task.CompletedTask; } });
        }

        return Task.FromResult<IReadOnlyList<CodeLens>>(lenses);
    }
}

/// <summary>X part 9: snippets (the syntax, the editor's stops, mirrors and choices, C#'s set, the user's files).</summary>
[TestGroup("Snippets")]
public static class SnippetTests
{
    private sealed class TestSnippets(params Snippet[] snippets) : ISnippetProvider
    {
        public string Name => "snippets-test";

        public string Summary => "Test: fixed snippets";

        public IReadOnlyList<Snippet> SnippetsFor(string? language) => snippets;
    }

    [Test]
    public static void TheParserExpandsStopsPlaceholdersChoicesVariablesAndEscapes()
    {
        var plain = SnippetParser.Expand("for (int ${1:i} = 0; $1 < ${2:n}; $1++)\n{\n\t$0\n}", new SnippetContext { Indent = "  ", IndentUnit = "    " });
        Check.Equal("for (int i = 0; i < n; i++)\n  {\n      \n  }", plain.Text, "placeholders, the line's indent on the next lines, a tab as the editor's indent");
        Check.SequenceEqual([1, 2, 0], plain.Order, "Tab visits 1, 2, then 0");
        Check.Equal(3, plain.Stops.Count(s => s.Index == 1), "stop 1 at its three places");
        Check.True(plain.Stops.First(s => s.Index == 1) is { Start: 9, Length: 1 }, "the placeholder's place and length");
        Check.True(plain.Stops.Single(s => s.Index == 0).Start == plain.Text.IndexOf("\n  }", StringComparison.Ordinal), "$0 on the empty line");

        var choice = SnippetParser.Expand("${1|public,private|} void ${2:M}()");
        Check.Equal("public void M()", choice.Text, "a choice inserts its first option");
        Check.SequenceEqual(["public", "private"], choice.Stops.First(s => s.Index == 1).Choices, "with its options kept");
        Check.True(choice.Stops.Any(s => s.Index == 0 && s.Start == choice.Text.Length), "no $0: one at the end");

        var nested = SnippetParser.Expand("${1:a ${2:b} c}$0");
        Check.Equal("a b c", nested.Text, "a placeholder inside a placeholder");
        Check.True(nested.Stops.First(s => s.Index == 1) is { Start: 0, Length: 5 } && nested.Stops.First(s => s.Index == 2) is { Start: 2, Length: 1 }, "both stops, the outer one around the inner");

        var variables = SnippetParser.Expand("class ${1:$TM_FILENAME_BASE} {} // ${UNKNOWN:none} $UNSET \\$1 \\} ${2/(.*)/$1/}", new SnippetContext
        {
            Variables = name => name == "TM_FILENAME_BASE" ? "Widget" : null,
        });
        Check.Equal("class Widget {} // none UNSET $1 } ", variables.Text, "a variable's value, an unknown one's default, else its name; escapes; a transform mirrors its stop");
    }

    [Test]
    public static void ACommittedSnippetFillsItsStopsWithTabMirrorsAndChoicesInOneUndoStep()
    {
        var provider = new TestSnippets(new Snippet("for loop", ["fori"], "for (int ${1:i} = 0; $1 < ${2|10,20|}; $1++)\n{\n\t$0\n}", "A for loop"));
        SnippetProviders.Register(provider);
        try
        {
            var (model, document, _) = Editors.Make("  fo", new CraftOptions { CompletionDelayMilliseconds = 0 });
            model.Select(4, 4);
            model.OnKey(EditorKey.Space, KeyModifiers.Control);
            Check.True(model.Completion.IsOpen, "Ctrl+Space lists the snippets of a document with no semantic layer at once");
            Check.True(model.Completion.Selected is { Label: "fori", Kind: CompletionItemKind.Snippet, Detail: "A for loop" }, "the snippet, with its own kind (its icon) and its description");
            model.OnKey(EditorKey.Enter, KeyModifiers.None);
            Check.Equal("  for (int i = 0; i < 10; i++)\n  {\n      \n  }", model.Snapshot.GetText(0, model.Snapshot.Length), "expanded in place of the word, indented as its line");
            Check.True(model.ActiveSnippet is not null, "the snippet is being filled in");
            Check.Equal(3, model.Selections.Length, "stop 1 selected at its three places");
            Check.True(model.Selections.ToArray().All(s => model.Snapshot.GetText(s.Start, s.Length) == "i"), "each holds the placeholder");

            model.OnText("k");
            Check.Equal("  for (int k = 0; k < 10; k++)\n  {\n      \n  }", model.Snapshot.GetText(0, model.Snapshot.Length), "typing fills every place");
            model.OnKey(EditorKey.Tab, KeyModifiers.None);
            Check.True(model.Completion.IsOpen && model.Completion.Items.Select(i => i.Label).SequenceEqual(["10", "20"]), "Tab: stop 2, its choices listed");
            model.OnKey(EditorKey.Down, KeyModifiers.None);
            model.OnKey(EditorKey.Enter, KeyModifiers.None);
            Check.True(model.Snapshot.GetText(0, model.Snapshot.Length).Contains("k < 20;", StringComparison.Ordinal), "the choice picked replaces the stop");
            model.OnKey(EditorKey.Tab, KeyModifiers.None);
            Check.Equal(model.Snapshot.GetText(0, model.Snapshot.Length).IndexOf("\n  }", StringComparison.Ordinal), model.Selection.Caret, "Tab: the final caret, on the empty line");
            Check.True(model.ActiveSnippet is null, "and the snippet is done");

            // Undo takes the edits back one step each: the choice, the typing at three places, the insertion.
            model.OnKey(EditorKey.Z, KeyModifiers.Control);
            Check.True(model.Snapshot.GetText(0, model.Snapshot.Length).Contains("k < 10;", StringComparison.Ordinal), "Undo: the choice");
            model.OnKey(EditorKey.Z, KeyModifiers.Control);
            Check.Equal("  for (int i = 0; i < 10; i++)\n  {\n      \n  }", model.Snapshot.GetText(0, model.Snapshot.Length), "Undo: the typing at its three places, one step");
            model.OnKey(EditorKey.Z, KeyModifiers.Control);
            Check.Equal("  fo", model.Snapshot.GetText(0, model.Snapshot.Length), "Undo: the insertion, one step");
            Check.True(document.CanRedo, "and Redo can bring it back");

            // Escape leaves a snippet; the carets go back to one.
            model.InsertSnippet("f(${1:a}, ${1:a})", 0, 4);
            Check.Equal(2, model.Selections.Length, "a mirrored stop at both places");
            model.OnKey(EditorKey.Escape, KeyModifiers.None);
            Check.True(model.ActiveSnippet is null && model.Selections.Length == 1, "Escape leaves it with one caret");
        }
        finally
        {
            SnippetProviders.Unregister(provider.Name);
        }
    }

    [Test]
    public static void CSharpsSetHasTheEverydaySnippetsAndCtorTakesTheFilesName()
    {
        var prefixes = CSharpSnippetSet.All.SelectMany(s => s.Prefixes).ToHashSet(StringComparer.Ordinal);
        foreach (var prefix in (ReadOnlySpan<string>)["prop", "propfull", "ctor", "class", "interface", "record", "for", "foreach", "if", "else", "while", "switch", "try", "tryf", "using", "cw", "test"])
        {
            Check.True(prefixes.Contains(prefix), $"C#'s set has {prefix}");
        }

        Check.True(new CSharpSnippetSet().SnippetsFor("csharp").Count == CSharpSnippetSet.All.Count && new CSharpSnippetSet().SnippetsFor("xml").Count == 0, "C#'s for C# only");
        var ctor = CSharpSnippetSet.All.First(s => s.Name == "ctor");
        var (model, _, _) = Editors.Make("class Widget\n{\n    ctor\n}", path: Path.Combine(Path.GetTempPath(), "Widget.cs"));
        int at = model.Snapshot.GetText(0, model.Snapshot.Length).IndexOf("ctor", StringComparison.Ordinal);
        model.InsertSnippet(ctor, at, at + 4);
        Check.Equal("class Widget\n{\n    public Widget()\n    {\n        \n    }\n}", model.Snapshot.GetText(0, model.Snapshot.Length), "ctor names the file's type, indented");
        Check.True(model.Selection is { IsEmpty: false } && model.Snapshot.GetText(model.Selection.Start, model.Selection.Length) == "Widget", "its name selected first");
        var test = CSharpSnippetSet.All.First(s => s.Prefixes.Contains("test"));
        Check.True(test.Body.StartsWith("[Fact]", StringComparison.Ordinal), "test is an xUnit fact");
    }

    [Test]
    public static async Task TheUsersSnippetsAreReadFromTheFoldersAndAgainWhenAFileIsSaved()
    {
        using var folder = new TempFolder();
        CSharpLanguage.Register();
        string user = Path.Combine(folder.Path, "user");
        Directory.CreateDirectory(user);
        string workspace = Path.Combine(folder.Path, "ws");
        Directory.CreateDirectory(Path.Combine(workspace, ".craft", "snippets"));
        await File.WriteAllTextAsync(Path.Combine(user, "csharp.json"), """
            {
              // comments and trailing commas, as VS Code's files have them
              "Guard": { "prefix": ["guard", "g"], "body": ["if (${1:x} is null)", "{", "\tthrow new ArgumentNullException(nameof($1));", "}"], "description": "A null guard", },
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(workspace, ".craft", "snippets", "team.code-snippets"), """
            { "Todo": { "prefix": "todo", "body": "// TODO($1): $0", "scope": "csharp,xml" }, "Any": { "prefix": "any", "body": "x" } }
            """);
        await File.WriteAllTextAsync(Path.Combine(workspace, ".craft", "snippets", "broken.json"), "{ not json");
        using var snippets = new UserSnippets();
        await snippets.SetFolders(user, workspace, null);
        var csharp = snippets.SnippetsFor("csharp");
        Check.SequenceEqual(["Guard", "Todo", "Any"], csharp.Select(s => s.Name).ToList(), "the user folder's language file, the workspace's scoped and unscoped ones");
        Check.SequenceEqual(["guard", "g"], csharp[0].Prefixes, "several prefixes");
        Check.True(csharp[0].Body.Contains("\tthrow", StringComparison.Ordinal) && csharp[0].Description == "A null guard", "the body's lines joined, the description");
        Check.SequenceEqual(["Todo", "Any"], snippets.SnippetsFor("xml").Select(s => s.Name).ToList(), "a language file is that language's only; the scope names others");
        Check.True(snippets.Problems.Count == 1 && snippets.Problems[0].Contains("broken.json", StringComparison.Ordinal), $"a file that does not parse is said ({string.Join("; ", snippets.Problems)})");

        int reads = snippets.Reads;
        await File.WriteAllTextAsync(Path.Combine(user, "csharp.json"), """{ "Log": { "prefix": "log", "body": "Log($0);" } }""");
        snippets.NoteSaved(Path.Combine(user, "csharp.json"));
        await TreeMoveTests.WaitFor(() => snippets.Reads > reads, "a saved snippets file is read again");
        await snippets.Loaded;
        Check.SequenceEqual(["Log", "Todo", "Any"], snippets.SnippetsFor("csharp").Select(s => s.Name).ToList(), "the new snippets replace the old");
        snippets.NoteSaved(Path.Combine(workspace, "Program.cs"));
        await snippets.Loaded;
        Check.Equal(reads + 1, snippets.Reads, "another file saved reads nothing");
    }
}

/// <summary>X part 9: inlay hints (C#'s from Roslyn's semantic model; the editor's request, cache, chips and columns).</summary>
[TestGroup("InlayHints")]
public static class InlayHintTests
{
    [Test]
    public static async Task CSharpNamesLiteralArgumentsAndShowsInferredTypesButNotWhatTheCodeAlreadySays()
    {
        using var project = new SemanticTests.Project();
        project.Replace("public static void Run()", "static void Show(int arg1, int arg2) { }\n    static void SetColor(string color) { }\n    public static void Run()");
        project.Replace("Console.WriteLine(circle.Area(2));", "Console.WriteLine(circle.Area(2));\n        Show(1, 2);\n        SetColor(\"red\");\n        var name = \"n\";\n        Circle.Make(radius: 3, name);\n        Func<int, int> twice = x => x * 2;\n        foreach (var each in new[] { 1 }) { }");
        var snapshot = project.Buffer.Current;
        var hints = await project.Session.InlayHintsAsync(snapshot, 0, snapshot.Length, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        string Describe(InlayHint h) => $"{h.Label}@{snapshot.GetText(h.Position, Math.Min(4, snapshot.Length - h.Position))}";
        var shown = hints.Select(Describe).ToList();
        Console.WriteLine("    hints: " + string.Join(", ", shown));
        Check.True(shown.Contains("radius:@1, \"") && shown.Contains("name:@\"a\")"), "parameter names before literal arguments");
        Check.True(shown.Contains("scale:@2));"), "a literal inside a nested call");
        Check.True(shown.Any(s => s.StartsWith(": Circle?@", StringComparison.Ordinal)), "the type inferred for var circle (nullable, as var infers it with nullable on), after its name");
        Check.True(shown.Any(s => s.StartsWith(": string@", StringComparison.Ordinal)) && shown.Any(s => s.StartsWith(": int@", StringComparison.Ordinal)), "var name and foreach's var each");
        Check.True(shown.Contains("int@x =>"), "an implicit lambda parameter's type before it");
        Check.True(!shown.Any(s => s.StartsWith("arg", StringComparison.Ordinal)), "none for parameters that differ only by a number");
        Check.True(!shown.Any(s => s.StartsWith("color:", StringComparison.Ordinal)), "none where the method's name ends with the parameter's");
        Check.True(hints.Count(h => h.Label == "name:") == 1, "none for a named argument nor for a variable of the parameter's name");
        Check.True(hints.All(h => h.Kind is InlayHintKind.Parameter or InlayHintKind.Type), "each with its kind");

        // Only the range asked.
        int run = project.At("Show(1, 2)");
        var some = await project.Session.InlayHintsAsync(snapshot, run, 10, CancellationToken.None);
        Check.True(some.All(h => h.Position >= run && h.Position <= run + 10), "only the range asked for");
    }

    private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> Ui = new();

    private static (EditorModel Model, HintLanguage.Session Session, EditorRenderer Renderer, RecordingCanvas Canvas) Make(string text, CraftOptions? options = null)
    {
        var language = new HintLanguage();
        var (model, _, _) = Editors.Make(text, options ?? new CraftOptions { InlayHintsDelayMilliseconds = 0, CodeLens = false }, language);
        model.Dispatch = Ui.Enqueue;                          // the answers land on "the UI thread": the test's, when it pumps
        return (model, language.Last!, new EditorRenderer(), new RecordingCanvas());
    }

    /// <summary>Runs what was dispatched to the UI thread until <paramref name="done"/> holds.</summary>
    internal static Task Landed(Func<bool> done, string what) => TreeMoveTests.WaitFor(() =>
    {
        while (Ui.TryDequeue(out var action))
        {
            action();
        }

        return done();
    }, what);

    [Test]
    public static async Task TheEditorAsksForTheLinesOnScreenOnceAVersionAndDrawsChipsThatMoveNoColumn()
    {
        var text = new StringBuilder();
        for (int i = 0; i < 200; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"call(1, 22) line {i}\n");
        }

        var (model, session, renderer, canvas) = Make(text.ToString());
        using var _ = renderer;
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        Check.Equal(1, session.HintRequests, "the first frame asks once (after the pause; 0 here)");
        var asked = session.Asked[0];
        Check.True(asked.Start == 0 && asked.Length < model.Snapshot.Length / 2, $"for the lines on screen and a screen below ({asked.Length} characters of {model.Snapshot.Length})");
        await Landed(() => model.InlayHints.Count > 0, "the answer lands on the UI thread");
        Check.True(model.InlayHints.All(h => h.Label == "n:"), "the answer kept");

        canvas.Clear();
        for (int i = 0; i < 5; i++)
        {
            renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        }

        Check.Equal(1, session.HintRequests, "frames over the same lines ask nothing more");
        Check.True(renderer.HintsDrawn > 0, "the chips are drawn");
        float cell = model.Metrics.CellWidth;
        float left = model.TextLeft;
        var texts = canvas.Texts.Where(t => t.Top == 0).ToList();
        Check.True(texts.Any(t => t.Text == "n:" && Math.Abs(t.X - (left + (5 * cell))) < 0.01), "a hint drawn where its argument was (column 5)");
        Check.True(texts.Any(t => t.Text.StartsWith('1') && Math.Abs(t.X - (left + (8 * cell))) < 0.01), "its argument moved right by the hint's cells (3)");

        // The caret's column is the text's; it is drawn before a hint at its place, and a click after the hint lands in the text.
        model.Select(5, 5);
        Check.Equal(5, model.CaretColumn, "the caret's column ignores the hint");
        canvas.Clear();
        renderer.DrawCarets(canvas, model, 0, 0, 800, 400);
        Check.True(canvas.Calls.Any(c => c.Kind == "fill" && Math.Abs(c.X - (left + (5 * cell))) < 0.01), "the caret is drawn before the hint at its place");
        int clicked = model.PositionFromPoint(left + (8.2f * cell), 1);
        Check.Equal(5, clicked, "a click on the argument the hint moved finds it");
        int onChip = model.PositionFromPoint(left + (6.2f * cell), 1);
        Check.Equal(5, onChip, "a click on the chip goes to its place");

        // An edit moves the kept hints until the new answer; the next frame asks for the new version.
        model.Select(0, 0);
        model.OnText("x");
        Check.True(model.InlayHints.Count > 0 && model.InlayHints[0].Position == 6, "the hints moved with the text");
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        Check.Equal(2, session.HintRequests, "the new version is asked for once");

        // Off, and Ctrl held.
        var (off, offSession, offRenderer, offCanvas) = Make("call(1)\n", new CraftOptions { InlayHints = "off", CodeLens = false });
        using var __ = offRenderer;
        offRenderer.Draw(offCanvas, off, 0, 0, 800, 400, caretVisible: true);
        Check.True(offSession.HintRequests == 0 && !off.InlayHintsShown, "off asks nothing");
        var (held, heldSession, heldRenderer, heldCanvas) = Make("call(1)\n", new CraftOptions { InlayHints = "ctrl-held", InlayHintsDelayMilliseconds = 0, CodeLens = false });
        using var ___ = heldRenderer;
        heldRenderer.Draw(heldCanvas, held, 0, 0, 800, 400, caretVisible: true);
        Check.Equal(0, heldSession.HintRequests, "ctrl-held: nothing without Ctrl");
        held.ControlHeld = true;
        heldRenderer.Draw(heldCanvas, held, 0, 0, 800, 400, caretVisible: true);
        Check.Equal(1, heldSession.HintRequests, "asked while Ctrl is held");
        await Landed(() => held.InlayHints.Count > 0, "the answer lands");
        heldRenderer.Draw(heldCanvas, held, 0, 0, 800, 400, caretVisible: true);
        Check.Equal(1, heldRenderer.HintsDrawn, "then drawn");
        held.ControlHeld = false;
        heldRenderer.Draw(heldCanvas, held, 0, 0, 800, 400, caretVisible: true);
        Check.Equal(0, heldRenderer.HintsDrawn, "and gone when Ctrl is let go");

        var (types, typesSession, typesRenderer, typesCanvas) = Make("call(1)\n", new CraftOptions { InlayHintsParameters = false, InlayHintsDelayMilliseconds = 0, CodeLens = false });
        using var ____ = typesRenderer;
        typesRenderer.Draw(typesCanvas, types, 0, 0, 800, 400, caretVisible: true);
        await Landed(() => types.InlayHintsRange is not null, "the answer lands");
        Check.True(typesSession.HintRequests == 1 && types.InlayHints.Count == 0, "a kind turned off is left out");
    }

    [Test]
    public static async Task AScreenOfAFiveThousandLineFileColdAndWarmAndAWarmFrameAllocatesNothing()
    {
        using var folder = new TempFolder();
        CSharpLanguage.Register();
        string path = folder.File("Big.cs");
        string code = LargeWithCalls(5000);
        await File.WriteAllTextAsync(path, code);
        var buffer = new PieceTableBuffer(code);
        using var session = new CSharpLanguage().Open(buffer, new LanguageOpenContext(path, folder.Path, LanguageSettings.Default with { DiagnosticsDelayMilliseconds = 60_000 }));
        var snapshot = buffer.Current;
        int start = snapshot.GetLine(2500).Start;
        int end = snapshot.GetLine(2541).End;
        var watch = Stopwatch.StartNew();
        var cold = await session.InlayHintsAsync(snapshot, start, end - start, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(3));
        double coldMs = watch.Elapsed.TotalMilliseconds;
        var times = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            watch.Restart();
            var again = await session.InlayHintsAsync(snapshot, start, end - start, CancellationToken.None);
            times.Add(watch.Elapsed.TotalMilliseconds);
            Check.Equal(cold.Count, again.Count, "the same hints each time");
        }

        times.Sort();
        Check.True(cold.Count > 20, $"a screen holds hints ({cold.Count})");
        await SymbolSamples.WriteMeasureAsync("part-9.txt", string.Create(CultureInfo.InvariantCulture,
            $"inlay hints, a screen (42 lines) of a {snapshot.LineCount}-line file: cold {coldMs:F1} ms (the workspace built, the compilation), warm median {times[2]:F2} ms, worst {times[^1]:F2} ms, {cold.Count} hints"));

        // The editor's warm frame with the hints shown: nothing asked, nothing allocated.
        var language = new HintLanguage();
        var lines = new StringBuilder();
        for (int i = 0; i < 5000; i++)
        {
            lines.Append(CultureInfo.InvariantCulture, $"call(1, 2) item({i}) more\n");
        }

        var (model, _, _) = Editors.Make(lines.ToString(), new CraftOptions { InlayHintsDelayMilliseconds = 0, CodeLens = false }, language);
        model.Dispatch = Ui.Enqueue;
        using var renderer = new EditorRenderer();
        using var canvas = new LayoutCanvas { Record = false };
        renderer.Draw(canvas, model, 0, 20_000, 1200, 800, caretVisible: true);
        await Landed(() => model.InlayHints.Count > 0, "the screen's hints land");
        renderer.Draw(canvas, model, 0, 20_000, 1200, 800, caretVisible: true);
        for (int i = 0; i < 200; i++)
        {
            renderer.Draw(canvas, model, 0, 20_000, 1200, 800, (i & 1) == 0);
        }

        long built = renderer.HintedLinesBuilt;
        long bytes = 0;
        int counted = 0;
        for (int i = 0; i < 500; i++)
        {
            int gcs = GC.CollectionCount(0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            renderer.Draw(canvas, model, 0, 20_000, 1200, 800, (i & 1) == 0);
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            if (GC.CollectionCount(0) == gcs)
            {
                bytes += used;
                counted++;
            }
        }

        Check.True(renderer.HintsDrawn > 40, $"the screen's hints drawn ({renderer.HintsDrawn})");
        Check.Equal(built, renderer.HintedLinesBuilt, "a warm frame builds no hinted line");
        Check.Equal(1, language.Last!.HintRequests, "and asks nothing");
        await SymbolSamples.WriteMeasureAsync("part-9.txt", string.Create(CultureInfo.InvariantCulture, $"inlay hints, a warm frame with {renderer.HintsDrawn} hints on screen: {bytes} B over {counted} frames"));
        Check.Equal(0L, bytes, "a warm frame with hints allocates nothing");
    }

    /// <summary>A C# file of about <paramref name="lines"/> lines of methods that call with literals and declare with var.</summary>
    internal static string LargeWithCalls(int lines)
    {
        var text = new StringBuilder("namespace Big;\n\npublic static class Calls\n{\n    public static int Use(int count, string name) => count;\n\n");
        int methods = (lines - 8) / 6;
        for (int m = 0; m < methods; m++)
        {
            text.Append(CultureInfo.InvariantCulture, $"    public static int M{m}()\n    {{\n        var total = Use({m}, \"m\");\n        return Use(total, \"t\");\n    }}\n\n");
        }

        text.Append("}\n");
        return text.ToString();
    }
}

/// <summary>X part 9: code lens (the rows above members, resolving on screen, clicks; C#'s references and tests).</summary>
[TestGroup("CodeLens")]
public static class CodeLensTests
{
    private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> UiQueue = new();

    private static Task Landed(Func<bool> done, string what) => TreeMoveTests.WaitFor(() =>
    {
        while (UiQueue.TryDequeue(out var action))
        {
            action();
        }

        return done();
    }, what);

    [Test]
    public static async Task LensRowsStandAboveMembersResolveOnScreenAndAClickRunsOne()
    {
        var provider = new HintLensProvider();
        CodeLensProviders.Register(provider);
        try
        {
            var text = new StringBuilder();
            for (int i = 0; i < 60; i++)
            {
                text.Append(CultureInfo.InvariantCulture, $"fn f{i}(1)\n  body\n");
            }

            var language = new HintLanguage();
            var (model, _, _) = Editors.Make(text.ToString(), new CraftOptions { CodeLensDelayMilliseconds = 0, InlayHints = "off" }, language);
            model.Dispatch = UiQueue.Enqueue;
            using var renderer = new EditorRenderer();
            var canvas = new RecordingCanvas();
            renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
            await Landed(() => model.CodeLenses.Count == 120, "the lenses of every member come (asked off the UI thread)");
            Check.Equal(1, provider.Requests, "asked once for the version");
            Check.Equal(60, model.LensLines.Length, "a lens row above each member");
            Check.Equal(1, model.RowOfLine(0), "line 0 sits below its lens row");
            Check.Equal(4, model.RowOfLine(2), "line 2 below two lens rows");
            Check.True(model.IsLensRow(0) && model.IsLensRow(3) && !model.IsLensRow(1), "rows 0 and 3 are lens rows");
            Check.True(model.LineAtRow(3) == 2 && model.LineAtRow(2) == 1, "a lens row gives the line below it; the lines stay numbered as lines");
            Check.Equal(model.Snapshot.LineCount + 60, model.RowCount, "the rows: the lines and the lens rows");

            canvas.Clear();
            renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
            Check.True(renderer.LensRowsDrawn > 0 && canvas.Texts.Any(t => t.Text.Contains("refs | Run", StringComparison.Ordinal) && t.Top == 0), "the lens row drawn on top, the titles joined");
            Check.True(canvas.Texts.Any(t => t.Text == "1" && Math.Abs(t.Top - model.Metrics.LineHeight) < 0.01), "line 1's number on the row below the lens row: numbers are the lines'");
            await Landed(() => model.CodeLenses.Any(l => l.Title == "2 refs"), "the lenses on screen are resolved");
            int resolved = provider.Resolved;
            Check.True(resolved > 0 && resolved < 60, $"only those on screen ({resolved} of 60)");

            // A click on "Run" runs it.
            var row = model.LensLines[0];
            float cell = model.Metrics.CellWidth;
            string drawn = canvas.Texts.First(t => t.Top == 0 && t.Text.Contains("Run", StringComparison.Ordinal)).Text;
            int runAt = drawn.IndexOf("Run", StringComparison.Ordinal);
            model.PointerPressed(model.TextLeft + ((runAt + 1) * cell), 1, KeyModifiers.None);
            model.PointerReleased();
            Check.Equal(1, provider.Runs, "a click on its title runs it");
            Check.Equal(0, row, "the first lens line");
            Check.Equal(0, model.Selection.Caret, "and the caret did not move");

            // The caret moves over lens rows as if they were not there.
            model.Select(model.Snapshot.GetLine(1).Start, model.Snapshot.GetLine(1).Start);
            model.OnKey(EditorKey.Down, KeyModifiers.None);
            Check.Equal(2, model.CaretLine, "Down goes from line 1 to line 2 (past the lens row)");
            model.OnKey(EditorKey.Up, KeyModifiers.None);
            Check.Equal(1, model.CaretLine, "and Up back");

            // An edit keeps the lenses (moved) and asks again.
            model.Select(0, 0);
            model.OnText("// x\n");
            Check.Equal(60, model.LensLines.Length, "the lenses moved with their members");
            Check.Equal(1, model.LensLines[0], "one line down");
            renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
            await Landed(() => provider.Requests == 2, "the new version is asked for");

            var (off, _, _) = Editors.Make(text.ToString(), new CraftOptions { CodeLens = false }, new HintLanguage());
            using var offRenderer = new EditorRenderer();
            offRenderer.Draw(new RecordingCanvas(), off, 0, 0, 800, 400, caretVisible: true);
            Check.True(off.LensLines.Length == 0 && !off.CodeLensShown, "CRAFT_CODE_LENS off: no rows");
        }
        finally
        {
            CodeLensProviders.Unregister(provider.Name);
        }
    }

    [Test]
    public static async Task CSharpCountsReferencesAndRunsATestThroughTheHost()
    {
        using var project = new SemanticTests.Project();
        project.Replace("public static void Run()", "[Fact]\n    public static void Adds() { }\n\n    public static void Run()");
        var symbols = await project.Session.GetDocumentSymbolsAsync(project.Buffer.Current, CancellationToken.None);
        string? ran = null;
        using var host = new EditorHost { RunTests = (_, test, _) => { ran = test; return Task.FromResult(true); } };
        var request = new CodeLensRequest(project.Buffer.Current, CSharpLanguage.RegularName, project.Session, project.ProgramPath, symbols) { Host = host };
        var lenses = await new CSharpCodeLens().GetLensesAsync(request, CancellationToken.None);
        Console.WriteLine("    lenses: " + string.Join(", ", lenses.Select(l => l.Title)));
        int program = project.At("public static class Program");
        Check.True(lenses.Any(l => l.Start == program && l.Title == "… references" && l.ResolveAsync is not null), "a type gets a references lens to resolve");
        var run = lenses.FirstOrDefault(l => l.Title == "Run test");
        Check.True(run is not null && lenses.Any(l => l.Title == "Debug test"), "the [Fact] method gets Run test and Debug test");
        await run!.RunAsync!(CancellationToken.None);
        Check.Equal("Demo.Program.Adds", ran, "Run test asks the host with the test's full name");
        var debug = lenses.First(l => l.Title == "Debug test");
        var said = Check.Throws<InvalidOperationException>(() => debug.RunAsync!(CancellationToken.None).GetAwaiter().GetResult(), "Debug test");
        Check.True(said.Message.Contains("no debugger", StringComparison.Ordinal), "Debug test says there is no debugger");

        var shapes = new PieceTableBuffer((await File.ReadAllTextAsync(project.ShapesPath)).ReplaceLineEndings("\n"));
        using var shapesSession = new CSharpLanguage().Open(shapes, new LanguageOpenContext(project.ShapesPath, project.Root, LanguageSettings.Default));
        var shapeSymbols = await shapesSession.GetDocumentSymbolsAsync(shapes.Current, CancellationToken.None);
        var shapeLenses = await new CSharpCodeLens().GetLensesAsync(new CodeLensRequest(shapes.Current, CSharpLanguage.RegularName, shapesSession, project.ShapesPath, shapeSymbols), CancellationToken.None);
        int make = shapes.Current.GetText(0, shapes.Current.Length).IndexOf("public static Circle Make", StringComparison.Ordinal);
        var makeLens = shapeLenses.Single(l => l.Start == make && l.ResolveAsync is not null);
        var resolved = await makeLens.ResolveAsync!(CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        Check.Equal("1 reference", resolved.Title, "Make is called once (Program.cs), its declaration not counted");
        var area = shapeLenses.Single(l => l.Start == shapes.Current.GetText(0, shapes.Current.Length).IndexOf("public double Area", StringComparison.Ordinal) && l.ResolveAsync is not null);
        Check.Equal("1 reference", (await area.ResolveAsync!(CancellationToken.None)).Title, "Area once too");
        Check.True(shapeLenses.All(l => l.Title != "Run test"), "no test there");
    }

    [Test]
    public static void TheDotnetProviderRunsOneTestWithAFilter()
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("Tests.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup>
            </Project>
            """);
        var provider = new DotnetRunProvider();
        var targets = provider.Find(new RunWorkspace(folder.Path, ["Tests.csproj"]));
        var target = targets.Single(t => (t.Actions & RunActions.Test) != 0);
        var command = provider.TestCommand(target, "App.Tests.MathTests.Adds", folder.Path);
        Check.True(command is not null, "a test project runs one test");
        if (command!.Problem is null)
        {
            Check.True(command.Command.Contains("test") && command.Command.SequenceEqual(command.Command.Take(command.Command.Count - 2).Concat(["--filter", "FullyQualifiedName=App.Tests.MathTests.Adds"])),
                $"dotnet test with the test's filter ({command.Line})");
        }
        else
        {
            Check.True(command.Problem.Contains("SDK", StringComparison.Ordinal), $"without the SDK it says so ({command.Problem})");
        }

        Check.True(provider.TestCommand(target with { Actions = RunActions.Build }, "X", folder.Path) is null, "a target that does not test runs none");
        Check.True(RunProviders.Get(RunProviders.Scripts).TestCommand(target, "X", folder.Path) is null, "the scripts provider picks no test (the default)");
    }
}

/// <summary>X part 9: linked tag rename in markup (the ranges from the tokenizer's states; the editor's one-step edit).</summary>
[TestGroup("LinkedTags")]
public static class LinkedTagTests
{
    private static ILanguage Markup(string name) => ProjectLanguages.All.First(l => l.Name == name);

    private static LinkedEditingRanges? At(ILanguageSession session, PieceTableBuffer buffer, string text, string marker, int offset = 1) =>
        session.GetLinkedEditingRanges(buffer.Current, text.IndexOf(marker, StringComparison.Ordinal) + offset, 5000);

    private static string Of(PieceTableBuffer buffer, TextRange range) => buffer.Current.GetText(range.Start, range.Length);

    [Test]
    public static void StartAndEndTagNamesAreFoundThroughNestingCommentsValuesAndEmptyElements()
    {
        const string text = """
            <Project>
              <!-- <Item> in a comment -->
              <Item a="<Item>">
                <Item/>
                <Item b='x'
                      c="y">
                  <Other />
                </Item>
                <![CDATA[ </Item> ]]>
              </Item>
            </Project>
            """;
        var buffer = new PieceTableBuffer(text);
        using var session = Markup(ProjectLanguages.Xml).Open(buffer);
        var outer = At(session, buffer, text, "<Item a=", 2);
        Check.True(outer is { Ranges.Count: 2 }, "the outer Item's start tag has a partner");
        int outerEnd = text.LastIndexOf("</Item>", StringComparison.Ordinal) + 2;
        Check.Equal(outerEnd, outer!.Ranges[1].Start, "its own end tag, past the nested ones, the comment's and the CDATA's");
        Check.Equal(text.IndexOf("<Item a=", StringComparison.Ordinal) + 1, outer.Ranges[0].Start, "the range asked about first");
        Check.Equal("Item", Of(buffer, outer.Ranges[1]), "the name");

        var back = session.GetLinkedEditingRanges(buffer.Current, outerEnd + 2, 5000);
        Check.True(back is { Ranges.Count: 2 } && back.Ranges[1].Start == outer.Ranges[0].Start, "from the end tag back to its start tag");
        var multiline = At(session, buffer, text, "<Item b=", 2);
        Check.True(multiline is { Ranges.Count: 2 } && multiline.Ranges[1].Start == text.IndexOf("</Item>", text.IndexOf("<Other", StringComparison.Ordinal), StringComparison.Ordinal) + 2, "a start tag over two lines finds its end");
        Check.True(At(session, buffer, text, "<Item/>", 2) is null, "an empty element has no partner");
        Check.True(At(session, buffer, text, "<!-- <Item>", 7) is null, "a tag in a comment is no tag");
        Check.True(At(session, buffer, text, "a=\"<Item>", 5) is null, "nor one in a value");
        Check.True(session.GetLinkedEditingRanges(buffer.Current, text.IndexOf("comment", StringComparison.Ordinal), 5000) is null, "plain text has none");
        Check.True(session.GetLinkedEditingRanges(buffer.Current, text.IndexOf("<Item a", StringComparison.Ordinal) + 2, 1) is null, "a partner beyond the lines allowed is not looked for");

        // Razor: HTML's void elements open nothing.
        const string razor = "<div>\n  <br>\n  <input type=\"x\" />\n  <p>@(a < b)</p>\n</div>";
        var razorBuffer = new PieceTableBuffer(razor);
        using var razorSession = Markup(ProjectLanguages.Razor).Open(razorBuffer);
        var div = At(razorSession, razorBuffer, razor, "<div>");
        Check.True(div is { Ranges.Count: 2 } && div.Ranges[1].Start == razor.IndexOf("</div>", StringComparison.Ordinal) + 2, "div past a void br and input, and C#'s '<'");
        Check.True(At(razorSession, razorBuffer, razor, "<br>") is null, "a void element has no partner");
    }

    [Test]
    public static void TypingInAStartTagsNameRenamesItsEndTagInOneUndoStep()
    {
        const string text = "<Items>\n  <Item>x</Item>\n</Items>";
        var (model, document, _) = Editors.Make(text, null, Markup(ProjectLanguages.Xml));
        int caret = text.IndexOf("<Item>", StringComparison.Ordinal) + 5;
        model.Select(caret, caret);
        model.OnText("s");
        Check.Equal("<Items>\n  <Items>x</Items>\n</Items>", model.Snapshot.GetText(0, model.Snapshot.Length), "the end tag follows");
        Check.True(!model.HasMultipleCarets && model.Selection.Caret == caret + 1, "one caret, after what was typed");
        Check.Equal(1, model.LinkedEdits, "one linked keystroke");
        model.OnKey(EditorKey.Backspace, KeyModifiers.None);
        Check.Equal(text, model.Snapshot.GetText(0, model.Snapshot.Length), "Backspace in the name: both");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("<Items>\n  <Items>x</Items>\n</Items>", model.Snapshot.GetText(0, model.Snapshot.Length), "Undo takes back both tags' change as one step");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal(text, model.Snapshot.GetText(0, model.Snapshot.Length), "and the first");
        Check.True(document.CanRedo, "two steps in all");

        // From the end tag; a space is not a name's character; at the name's start Backspace joins nothing.
        int end = text.IndexOf("</Item>", StringComparison.Ordinal) + 6;
        model.Select(end, end);
        model.OnText("X");
        Check.Equal("<Items>\n  <ItemX>x</ItemX>\n</Items>", model.Snapshot.GetText(0, model.Snapshot.Length), "typing in the end tag renames the start tag");
        int name = model.Snapshot.GetText(0, model.Snapshot.Length).IndexOf("<ItemX>", StringComparison.Ordinal) + 6;
        model.Select(name, name);
        model.OnText(" ");
        Check.Equal("<Items>\n  <ItemX >x</ItemX>\n</Items>", model.Snapshot.GetText(0, model.Snapshot.Length), "a space starts an attribute: only here");

        var (off, _, _) = Editors.Make(text, new CraftOptions { LinkedEditing = false }, Markup(ProjectLanguages.Xml));
        off.Select(caret, caret);
        off.OnText("s");
        Check.Equal("<Items>\n  <Items>x</Item>\n</Items>", off.Snapshot.GetText(0, off.Snapshot.Length), "CRAFT_LINKED_EDITING off: only the start tag");
    }
}

/// <summary>X part 9: the Outline view.</summary>
[TestGroup("Outline")]
public static class OutlineTests
{
    [Test]
    public static async Task TheOutlineShowsTheActiveEditorsTreeFollowsTheCaretFiltersSortsAndGoes()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        CSharpLanguage.Register();
        var path = fixture.Write("Circle.cs", SymbolSamples.Outline.ReplaceLineEndings("\n"));
        var shell = await fixture.OpenWorkspaceAsync();
        Check.True(EditorRailItems.All.Any(r => r is { Id: OutlineView.PanelId, Icon: RailIcons.Outline }) && fixture.Host.Panels.Any(p => p.Id == OutlineView.PanelId), "the Outline is a side view of the rail");
        var outline = shell.Outline;
        Check.True(outline.Blocks.OfType<PanelResults>().Single().Empty!.Contains("Open a file", StringComparison.Ordinal), "with no editor: an empty state that says what to do");

        var tab = (await shell.OpenFileAsync(path))!;
        var editor = fixture.EditorOf(tab);
        outline.Refresh();
        await outline.Loaded;
        await TreeMoveTests.WaitFor(() => outline.Symbols is { Count: > 0 }, "the symbols come off the UI thread");
        var names = outline.Rows.Select(r => outline.Symbols!.Names[r.Symbol]).ToList();
        Check.True(names.Take(3).SequenceEqual(["Demo.Shapes", "Circle", "Pi"]), $"in the order of the text, namespace, type, members ({string.Join(", ", names.Take(6))})");
        var results = outline.Blocks.OfType<PanelResults>().Single();
        var circleRow = results.Groups[0].Rows.First(r => r.Text == "Circle");
        Check.True(circleRow is { Depth: 1, Expanded: true, ToggleActionId: not null, Prefix: not null } && results.Groups[0].Rows.First(r => r.Text == "Pi").Depth == 2, "a tree: depths, open nodes, the kind's badge");

        // Following the caret.
        int scale = editor.Snapshot.GetText(0, editor.Snapshot.Length).IndexOf("by;", StringComparison.Ordinal);
        editor.Select(scale, scale);
        outline.Refresh();
        Check.Equal("Scale", outline.Symbols!.Names[outline.SelectedSymbol], "the symbol holding the caret is highlighted");
        Check.True(outline.Blocks.OfType<PanelResults>().Single().Groups[0].Rows.Single(r => r.Selected).Text == "Scale", "its row selected (and revealed by the host)");

        // A filter keeps the matches and their parents; sorting by name; closing a node.
        await outline.ActAsync("outline.filter", "sc");
        var filtered = outline.Rows.Select(r => outline.Symbols!.Names[r.Symbol]).ToList();
        Check.True(filtered.SequenceEqual(["Demo.Shapes", "Circle", "Scale"]), $"the match with its parents ({string.Join(", ", filtered)})");
        await outline.ActAsync("outline.filter", "");
        await outline.ActAsync("outline.sort", "name");
        var top = outline.Rows.Where(r => r.Depth == 1).Select(r => outline.Symbols!.Names[r.Symbol]).ToList();
        Check.True(top.SequenceEqual(top.Order(StringComparer.OrdinalIgnoreCase)), $"by name ({string.Join(", ", top)})");
        await outline.ActAsync("outline.sort", "kind");
        Check.Equal("kind", outline.Sort, "by kind");
        int before = outline.Rows.Count;
        await outline.ActAsync("outline.toggle", "Demo.Shapes/Circle");
        Check.True(outline.Rows.Count < before && outline.Rows.All(r => outline.Symbols!.Parents[r.Symbol] < 0 || outline.Symbols.Names[outline.Symbols.Parents[r.Symbol]] != "Circle"), "a closed node hides its children");

        // A click goes to the symbol's name.
        int area = outline.Symbols!.IndexOf("Area");
        await outline.ActAsync("outline.go", area.ToString(CultureInfo.InvariantCulture));
        Check.Equal("Area", editor.Snapshot.GetText(editor.Selection.Start, editor.Selection.Length), "the name selected in the editor");

        // A language without symbols.
        var plain = fixture.Write("notes.grp", "no symbols here");
        var plainTab = (await shell.OpenFileAsync(plain))!;
        fixture.EditorOf(plainTab);
        outline.Refresh();
        await outline.Loaded;
        Check.True(outline.Blocks.OfType<PanelResults>().Single().Empty!.Contains("no symbols", StringComparison.Ordinal), $"a language with none says so ({outline.Blocks.OfType<PanelResults>().Single().Empty})");
    }
}

/// <summary>X part 9: the call and type hierarchies.</summary>
[TestGroup("Hierarchy")]
public static class HierarchyTests
{
    private const string Sources = """
        namespace Demo;

        public interface IShape { double Area(); }

        public abstract class Shape : IShape { public abstract double Area(); }

        public sealed class Circle : Shape
        {
            public override double Area() => Pi() * 2;

            private static double Pi() => 3.14;

            public Circle() { }

            public static Circle Make() => new Circle();
        }

        public static class Program
        {
            public static void Run()
            {
                var circle = Circle.Make();
                System.Console.WriteLine(circle.Area());
                Helper();
            }

            private static void Helper()
            {
                Circle.Make();
                Circle.Make();
            }
        }
        """;

    [Test]
    public static async Task CallersCallsBaseAndDerivedTypesAreWalkedOneLevelAtATime()
    {
        using var project = new HierarchyProject();
        var snapshot = project.Buffer.Current;
        var make = await project.Session.PrepareHierarchyAsync(snapshot, project.At("Make()"), HierarchyKind.Call, CancellationToken.None).WaitAsync(TimeSpan.FromMinutes(2));
        Check.True(make is { Name: "Make()", Kind: ScopeKind.Method, Detail: "Demo.Circle" } && make.Line > 0, $"the root: Make, in Circle ({make})");
        var callers = await project.Session.HierarchyAsync(make!, HierarchyDirection.Incoming, CancellationToken.None);
        Check.True(callers.Select(c => c.Name).Order().SequenceEqual(["Helper()", "Run()"]), $"its callers ({string.Join(", ", callers.Select(c => c.Name))})");
        var helper = callers.Single(c => c.Name == "Helper()");
        Check.True(helper.Calls.Count == 2 && helper.CallsFile == project.ProgramPath, "Helper calls it twice, each place kept");

        var run = callers.Single(c => c.Name == "Run()");
        var calls = await project.Session.HierarchyAsync(run, HierarchyDirection.Outgoing, CancellationToken.None);
        var called = calls.Select(c => c.Name).ToList();
        Check.True(called.Contains("Make()") && called.Contains("Area()") && called.Contains("Helper()") && calls.Any(c => c.IsMetadata && c.Name.StartsWith("WriteLine", StringComparison.Ordinal)), $"what Run calls, a metadata member too ({string.Join(", ", called)})");

        var circle = await project.Session.PrepareHierarchyAsync(snapshot, project.At("Circle : Shape"), HierarchyKind.Type, CancellationToken.None);
        Check.True(circle is { Name: "Circle", Kind: ScopeKind.Type }, "a type hierarchy's root");
        var bases = await project.Session.HierarchyAsync(circle!, HierarchyDirection.Supertypes, CancellationToken.None);
        Check.True(bases.Select(b => b.Name).SequenceEqual(["Shape"]), $"Circle's base ({string.Join(", ", bases.Select(b => b.Name))})");
        var shapeBases = await project.Session.HierarchyAsync(bases[0], HierarchyDirection.Supertypes, CancellationToken.None);
        Check.True(shapeBases.Select(b => b.Name).SequenceEqual(["IShape"]), "Shape's interface");
        var derived = await project.Session.HierarchyAsync(shapeBases[0], HierarchyDirection.Subtypes, CancellationToken.None);
        Check.True(derived.Any(d => d.Name == "Shape"), $"IShape's implementations ({string.Join(", ", derived.Select(d => d.Name))})");
        var fromShape = await project.Session.HierarchyAsync(bases[0], HierarchyDirection.Subtypes, CancellationToken.None);
        Check.True(fromShape.Select(d => d.Name).SequenceEqual(["Circle"]), "Shape's derived class");
        Check.True(await project.Session.PrepareHierarchyAsync(snapshot, project.At("namespace Demo") + 2, HierarchyKind.Call, CancellationToken.None) is null, "no member at the caret: none");
    }

    // A temporary project of one file, Sources, with its session.
    private sealed class HierarchyProject : IDisposable
    {
        private readonly TempFolder _folder = new();

        public HierarchyProject()
        {
            CSharpLanguage.Register();
            File.WriteAllText(_folder.File("Demo.csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>{Craft.CSharp.Workspaces.ReferenceAssemblies.RunningFramework}</TargetFramework></PropertyGroup>
                </Project>
                """);
            string text = Sources.ReplaceLineEndings("\n");
            File.WriteAllText(ProgramPath, text);
            Buffer = new PieceTableBuffer(text);
            Session = new CSharpLanguage().Open(Buffer, new LanguageOpenContext(ProgramPath, _folder.Path, LanguageSettings.Default with { DiagnosticsDelayMilliseconds = 60_000 }));
        }

        public string ProgramPath => _folder.File("Program.cs");

        public PieceTableBuffer Buffer { get; }

        public CSharpLanguageSession Session { get; }

        public int At(string what) => Buffer.Current.GetText(0, Buffer.Current.Length).IndexOf(what, StringComparison.Ordinal);

        public void Dispose()
        {
            Session.Dispose();
            _folder.Dispose();
        }
    }

    [Test]
    public static async Task TheHierarchyTabOpensLazilyAsActivitiesAndAClickGoesToTheCall()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        CSharpLanguage.Register();
        fixture.Write("Demo.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>{Craft.CSharp.Workspaces.ReferenceAssemblies.RunningFramework}</TargetFramework></PropertyGroup>
            </Project>
            """);
        var program = fixture.Write("Program.cs", Sources.ReplaceLineEndings("\n"));
        var shell = await fixture.OpenWorkspaceAsync();
        var tab = (await shell.OpenFileAsync(program))!;
        var editor = fixture.EditorOf(tab);
        string text = editor.Snapshot.GetText(0, editor.Snapshot.Length);
        int make = text.IndexOf("Make()", StringComparison.Ordinal) + 1;
        editor.Select(make, make);
        string? asked = null;
        shell.ViewRequested += (_, view) => asked = view;
        Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.CallHierarchy), "Shift+Alt+H runs");
        await shell.Hierarchy.Pending.WaitAsync(TimeSpan.FromMinutes(2));
        Check.Equal(ShellViewNames.Hierarchy, asked, "the bottom panel's Hierarchy tab is asked for");
        var rows = shell.Hierarchy.Rows;
        Check.True(rows.Count == 3 && rows[0] is { Depth: 0, Item.Name: "Make()" } && rows.Skip(1).All(r => r.Depth == 1), $"the root and its callers, open ({string.Join(", ", rows.Select(r => r.Item.Name))})");
        Check.True(fixture.Host.ActivityCenter.Recent.Any(a => a.Title == "Call hierarchy") && fixture.Host.ActivityCenter.Recent.Any(a => a.Title.StartsWith("Hierarchy: callers of Make", StringComparison.Ordinal)), "each request was an activity");

        var run = rows.Single(r => r.Item.Name == "Run()");
        Check.True(shell.Hierarchy.Rows.Count == 3, "a caller's own callers are not asked yet");
        await shell.Hierarchy.ToggleAsync(run.Id);
        await shell.Hierarchy.Pending;
        Check.True(shell.Hierarchy.Rows.Count == 3, "Run has no callers: opened, nothing under it");
        var block = shell.Hierarchy.Blocks.OfType<PanelResults>().Single();
        Check.True(block.Groups[0].Title == "Callers of Make()" && block.Groups[0].Rows[0].Expanded == true, "the tab's title and its open root");

        var helper = shell.Hierarchy.Rows.Single(r => r.Item.Name == "Helper()");
        await shell.Hierarchy.ActAsync("hierarchy.go", helper.Id);
        int call = text.IndexOf("Circle.Make();", text.IndexOf("void Helper", StringComparison.Ordinal), StringComparison.Ordinal);
        Check.True(editor.Selection.Caret >= call && editor.Selection.Caret <= call + 7, $"a caller's click goes to its first call (caret {editor.Selection.Caret}, the call at {call})");

        await shell.Hierarchy.ActAsync("hierarchy.direction", nameof(HierarchyDirection.Outgoing));
        await shell.Hierarchy.Pending;
        Check.True(shell.Hierarchy.Rows.Count == 2 && shell.Hierarchy.Rows[1].Item.Name == "Circle()", $"Make calls the constructor ({string.Join(", ", shell.Hierarchy.Rows.Select(r => r.Item.Name))})");

        int circle = text.IndexOf("class Circle", StringComparison.Ordinal) + 7;
        editor.Select(circle, circle);
        Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.TypeHierarchy), "the type hierarchy runs");
        await shell.Hierarchy.Pending.WaitAsync(TimeSpan.FromMinutes(2));
        Check.True(shell.Hierarchy is { Kind: HierarchyKind.Type, Direction: HierarchyDirection.Supertypes } && shell.Hierarchy.Rows.Select(r => r.Item.Name).SequenceEqual(["Circle", "Shape"]), "Circle and its base");

        foreach (var keyMap in new[] { "", StandardShortcuts.VisualStudio })
        {
            fixture.Host.SetKeyBindings([], keyMap);
            Check.Equal("Shift+Alt+H", fixture.Host.KeysOf(ShellCommandIds.CallHierarchy), $"Shift+Alt+H in {(keyMap.Length == 0 ? "VS Code's" : "Visual Studio's")} keymap");
        }

        var menu = EditorMenus.ItemsOf(EditorContextMenus.Editor).Select(m => m.CommandId).ToList();
        Check.True(menu.Contains(ShellCommandIds.TypeHierarchy) && menu.Contains(ShellCommandIds.CallHierarchy), "both in the editor's menu");
        Check.True(EditorCommands.Find(ShellCommandIds.TypeHierarchy) is { Title: "Go: Type hierarchy" }, "and in the palette");
    }
}

/// <summary>X part 9's speed fixes in the C# session.</summary>
[TestGroup("CSharp")]
public static class PartNineSpeedTests
{
    [Test]
    public static async Task AnIdleFileWithNoWorkspaceDocumentStartsNoColourPassEveryDelay()
    {
        CSharpLanguage.Register();
        var buffer = new PieceTableBuffer("class A { void M() { int x = 1; } }\n");
        var settings = LanguageSettings.Default with { WorkspaceReaders = [], SemanticColorsDelayMilliseconds = 5, DiagnosticsDelayMilliseconds = 60_000 };
        using var session = new CSharpLanguage().Open(buffer, new LanguageOpenContext(null, null, settings));
        await session.WhenIdle();
        using var spans = new TokenSpanBuffer();
        for (int i = 0; i < 40; i++)
        {
            spans.Clear();
            session.Classify(buffer.Current, 0, buffer.Current.Length, spans);
            await Task.Delay(10);
        }

        await session.WhenColorsIdle();
        int started = session.ColorsPassesStarted;
        Console.WriteLine($"    colour passes started over 40 frames 10 ms apart (5 ms delay): {started}");
        Check.True(started <= 1, $"one pass at most finds there is nothing to classify, then none ({started} started; before the fix one each delay)");
        buffer.Apply([new TextEdit(0, 0, " ")]);
        for (int i = 0; i < 10; i++)
        {
            spans.Clear();
            session.Classify(buffer.Current, 0, buffer.Current.Length, spans);
            await Task.Delay(10);
        }

        Check.True(session.ColorsPassesStarted <= started + 1, "an edit may try once more, no more");
    }

    [Test]
    public static async Task AKeystrokeMakesNoIdleSourceWhenNothingWaits()
    {
        using var project = new SemanticTests.Project(LanguageSettings.Default with { DiagnosticsDelayMilliseconds = 60_000, SemanticColorsDelayMilliseconds = -1 });
        await project.Session.WhenDiagnosticsIdle().WaitAsync(TimeSpan.FromMinutes(2));
        await project.Session.WhenIdle();
        int made = project.Session.IdleSourcesMade;
        int at = project.At("Console.WriteLine");
        for (int i = 0; i < 50; i++)
        {
            project.Buffer.Apply([new TextEdit(at + i, 0, "x")]);
        }

        Check.Equal(made, project.Session.IdleSourcesMade, "50 keystrokes (the parse worker woken, diagnostics due) made no idle source");
        var waiting = project.Session.WhenDiagnosticsIdle();
        Check.Equal(made + 1, project.Session.IdleSourcesMade, "one is made when someone waits");
        Check.True(!waiting.IsCompleted, "and it waits for the pass that is due");
        project.Session.EnableSemantics();
        await waiting.WaitAsync(TimeSpan.FromMinutes(2));
    }
}
