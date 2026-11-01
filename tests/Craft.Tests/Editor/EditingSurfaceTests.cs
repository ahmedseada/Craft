// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Settings;
using Craft.CSharp;
using Craft.Editor;
using Craft.Editor.Shell;
using Craft.Tests.Runner;
using Microsoft.CodeAnalysis.CSharp;

namespace Craft.Tests.Editor;

/// <summary>Editors over Craft.Core's real document (its undo keeps every caret), with the tests' fixed metrics.</summary>
internal static class RealEditors
{
    public static (EditorModel Model, TextDocument Document) Make(string text, CraftOptions? options = null, ILanguage? language = null)
    {
        options = (options ?? new CraftOptions()) with { NewLine = "\n", FoldingDelayMilliseconds = 60_000 };
        var document = TextDocument.Create(text, options);
        var model = new EditorModel(document, language, options, EditorThemes.Get(EditorThemes.Dark), new FixedMeasurer(), new FakeClipboard());
        model.UpdateViewport(0, 0, 800, 400);
        return (model, document);
    }

    public static string Text(ITextDocument document) => document.Buffer.Current.GetText(0, document.Buffer.Current.Length);

    /// <summary>The text with "|" at each caret (and "[", "]" around each selection).</summary>
    public static string Marked(EditorModel model)
    {
        var text = new StringBuilder(Text(model.Document));
        var all = model.Selections.ToArray();
        for (int i = all.Length - 1; i >= 0; i--)
        {
            var s = all[i];
            if (s.IsEmpty)
            {
                text.Insert(s.Caret, '|');
            }
            else
            {
                text.Insert(s.End, ']').Insert(s.Start, '[');
            }
        }

        return text.ToString();
    }
}

/// <summary>Folds from each '{' to its '}' on a later line (a test provider for the tests' quote language).</summary>
internal sealed class BraceFolding(string language) : IFoldingRangeProvider
{
    public int Calls { get; private set; }

    public string Name => "test-braces";

    public string Summary => "Test: braces";

    public IReadOnlyList<string> Languages { get; } = [language];

    public Task<IReadOnlyList<FoldingRange>> GetRangesAsync(FoldingRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<FoldingRange>>(Braces(request.Snapshot));
    }

    public static List<FoldingRange> Braces(ITextSnapshot snapshot)
    {
        var ranges = new List<FoldingRange>();
        var open = new Stack<int>();
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i] == '{')
            {
                open.Push(snapshot.GetLineIndex(i));
            }
            else if (snapshot[i] == '}' && open.Count > 0)
            {
                int first = open.Pop();
                int last = snapshot.GetLineIndex(i);
                if (last > first)
                {
                    ranges.Add(new FoldingRange(first, last));
                }
            }
        }

        return ranges;
    }
}

/// <summary>Phase X: several carets (Alt+click, Ctrl+D, select all occurrences, column selection), every edit one undo step.</summary>
[TestGroup("MultiCaret")]
public static class MultiCaretTests
{
    private const KeyModifiers None = KeyModifiers.None;

    [Test]
    public static void AltClickAddsCaretsAndTypingDeletingAndUndoActOnEvery()
    {
        var (model, document) = RealEditors.Make("ab\nab\nab");
        using var _ = model;
        model.Select(2, 2);
        model.PointerPressed(model.TextLeft + 20, 21, KeyModifiers.Alt);
        model.PointerReleased();
        model.PointerPressed(model.TextLeft + 20, 41, KeyModifiers.Alt);
        model.PointerReleased();
        Check.Equal(3, model.SelectionCount, "two Alt+clicks added two carets");
        Check.Equal(2, model.PrimarySelectionIndex, "the last caret added is the primary");
        model.OnText("x");
        model.OnText("y");
        Check.Equal("abxy|\nabxy|\nabxy|", RealEditors.Marked(model), "typing goes in at every caret");
        model.OnKey(EditorKey.Backspace, None);
        Check.Equal("abx|\nabx|\nabx|", RealEditors.Marked(model), "Backspace deletes at every caret");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("abxy|\nabxy|\nabxy|", RealEditors.Marked(model), "one undo takes the Backspace back, every caret restored");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("ab|\nab|\nab|", RealEditors.Marked(model), "the typing at three carets was one undo step");
        model.OnKey(EditorKey.Y, KeyModifiers.Control);
        Check.Equal("abxy|\nabxy|\nabxy|", RealEditors.Marked(model), "and one redo");
        model.OnKey(EditorKey.Enter, None);
        Check.Equal("abxy\n|\nabxy\n|\nabxy\n|", RealEditors.Marked(model), "Enter at every caret");
        model.OnKey(EditorKey.Left, None);
        Check.Equal("abxy|\n\nabxy|\n\nabxy|\n", RealEditors.Marked(model), "the arrows move every caret");
        model.PointerPressed(model.TextLeft + 40, 41, KeyModifiers.Alt);
        model.PointerReleased();
        Check.Equal(2, model.SelectionCount, "an Alt+click on a caret takes it away");
        Check.True(model.OnKey(EditorKey.Escape, None) && model.SelectionCount == 1, "Escape returns to one caret");
        Check.Equal(SelectionState.At(16), model.Selection, "the primary (the last caret added) stays");
    }

    [Test]
    public static void CtrlDAddsTheNextWholeWordAndSelectAllOccurrencesTakesEvery()
    {
        var (model, _) = RealEditors.Make("foo fool foo\nfoo");
        using var _m = model;
        model.Select(1, 1);
        model.AddNextOccurrence();
        Check.Equal("[foo] fool foo\nfoo", RealEditors.Marked(model), "the first Ctrl+D selects the word");
        model.AddNextOccurrence();
        Check.Equal("[foo] fool [foo]\nfoo", RealEditors.Marked(model), "the next whole word, not 'fool'");
        model.AddNextOccurrence();
        model.AddNextOccurrence();
        Check.Equal(3, model.SelectionCount, "every place taken; another Ctrl+D adds nothing");
        model.OnText("bar");
        Check.Equal("bar| fool bar|\nbar|", RealEditors.Marked(model), "typing replaces every selection");

        var (all, _) = RealEditors.Make("ab x ab x ab");
        using var _a = all;
        all.Select(0, 2);
        all.SelectAllOccurrences();
        Check.Equal("[ab] x [ab] x [ab]", RealEditors.Marked(all), "Ctrl+Shift+L selects every place of the selection");
        Check.Equal(0, all.PrimarySelectionIndex, "the selection it started from stays the primary");

        var (limited, _) = RealEditors.Make("a a a a a", new CraftOptions { MaxCarets = 3 });
        using var _l = limited;
        limited.Select(0, 1);
        limited.SelectAllOccurrences();
        Check.True(limited.SelectionCount == 3 && limited.LastCaretMessage!.Contains("CRAFT_MAX_CARETS", StringComparison.Ordinal), "the caret limit holds and is said");
    }

    [Test]
    public static void PasteGivesOneLinePerCaretAndCopyJoinsThem()
    {
        var (model, document) = RealEditors.Make("1\n2\n3", new CraftOptions { AutoCloseBrackets = true });
        using var _ = model;
        model.SetSelections([SelectionState.At(1), SelectionState.At(3), SelectionState.At(5)], 0);
        model.PasteText("a\nb\nc");
        Check.Equal("1a|\n2b|\n3c|", RealEditors.Marked(model), "as many lines as carets: one at each");
        model.PasteText("(");
        Check.Equal("1a(|\n2b(|\n3c(|", RealEditors.Marked(model), "a paste never auto-closes");
        model.PasteText("zz\nyy");
        Check.Equal("1a(zz\nyy|\n2b(zz\nyy|\n3c(zz\nyy|", RealEditors.Marked(model), "other counts: the whole text at every caret");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("1a(|\n2b(|\n3c(|", RealEditors.Marked(model), "one undo step");

        var clipboard = new FakeClipboard();
        var copied = new EditorModel(TextDocument.Create("one two\nthree"), null, new CraftOptions { NewLine = "\n", FoldingDelayMilliseconds = 60_000 }, null, new FixedMeasurer(), clipboard);
        using var _c = copied;
        copied.SetSelections([new SelectionState(0, 3), new SelectionState(8, 13)], 0);
        copied.OnKey(EditorKey.C, KeyModifiers.Control);
        Check.Equal("one\nthree", clipboard.Text, "Copy joins the selections, one per line");
    }

    [Test]
    public static void LineCommandsActOnEveryCaretsLinesAsOneStep()
    {
        var (model, document) = RealEditors.Make("a\nb\nc\nd", language: new QuoteLanguage());
        using var _ = model;
        model.SetSelections([SelectionState.At(0), SelectionState.At(4)], 0);
        model.ToggleLineComment();
        Check.Equal("// a\nb\n// c\nd", RealEditors.Text(document), "both carets' lines commented, the line between left");
        model.ToggleLineComment();
        Check.Equal("a\nb\nc\nd", RealEditors.Text(document), "and toggled back");
        model.MoveLines(LineDirection.Down);
        Check.Equal("b\na\nd\nc", RealEditors.Text(document), "Alt+Down moves every caret's line");
        Check.Equal("b\n|a\nd\n|c", RealEditors.Marked(model), "the carets go with their lines");
        model.OnKey(EditorKey.Z, KeyModifiers.Control);
        Check.Equal("|a\nb\n|c\nd", RealEditors.Marked(model), "one undo step");
        model.DuplicateLines();
        Check.Equal("a\n|a\nb\nc\n|c\nd", RealEditors.Marked(model), "duplicated below each, the carets on the copies");
        model.DeleteLines();
        Check.Equal("a\n|b\nc\n|d", RealEditors.Marked(model), "deleted at each");
        model.IndentLines();
        Check.Equal("a\n    |b\nc\n    |d", RealEditors.Marked(model), "Ctrl+] indents each caret's line");
        model.OutdentLines();
        Check.Equal("a\n|b\nc\n|d", RealEditors.Marked(model), "Ctrl+[ outdents them");
    }

    [Test]
    public static void ColumnSelectionByKeysAndByShiftAltDrag()
    {
        var (model, _) = RealEditors.Make("abcd\nabcd\nab\nabcd");
        using var _m = model;
        model.Select(1, 1);
        model.ColumnSelect(EditorKey.Down);
        model.ColumnSelect(EditorKey.Down);
        model.ColumnSelect(EditorKey.Down);
        model.ColumnSelect(EditorKey.Right);
        model.ColumnSelect(EditorKey.Right);
        Check.Equal("a[bc]d\na[bc]d\na[b]\na[bc]d", RealEditors.Marked(model), "a box from column 1 to 3, a short line clamped");
        Check.Equal(3, model.PrimarySelectionIndex, "the moving corner's line is the primary");
        model.OnText("X");
        Check.Equal("aX|d\naX|d\naX|\naX|d", RealEditors.Marked(model), "typing replaces the box on every line, as multi-caret");

        var (drag, _) = RealEditors.Make("abcd\nabcd\nabcd");
        using var _d = drag;
        drag.PointerPressed(drag.TextLeft + 10, 1, KeyModifiers.Shift | KeyModifiers.Alt);
        drag.PointerMoved(drag.TextLeft + 30, 41);
        drag.PointerReleased();
        Check.Equal("a[bc]d\na[bc]d\na[bc]d", RealEditors.Marked(drag), "Shift+Alt+drag draws the box");
        drag.OnKey(EditorKey.Delete, None);
        Check.Equal("a|d\na|d\na|d", RealEditors.Marked(drag), "Delete removes it on every line");
    }

    [Test]
    public static void AutoCloseAndOvertypeWorkAtEveryCaret()
    {
        var (model, _) = RealEditors.Make("\n", new CraftOptions { AutoCloseBrackets = true });
        using var _m = model;
        model.SetSelections([SelectionState.At(0), SelectionState.At(1)], 1);
        model.OnText("(");
        Check.Equal("(|)\n(|)", RealEditors.Marked(model), "closed at both");
        model.OnText("a");
        model.OnText(")");
        Check.Equal("(a)|\n(a)|", RealEditors.Marked(model), "the close typed over the auto-inserted ones");
        model.AddCaretVertical(LineDirection.Up);
        Check.Equal(2, model.SelectionCount, "Ctrl+Alt+Up at the top line adds nothing");
    }

    [Test]
    public static async Task TheKeymapsBindTheCaretAndFoldCommandsDifferently()
    {
        using var folder = new TempFolder();
        using var fixture = new SettingsFixture(folder);
        var (model, document, _) = Editors.MakeMarked("x |a x\nb a\nx", language: new QuoteLanguage());
        using var edit = new ShellEditCommands(() => model, fixture.Shell);
        var host = fixture.Host;
        host.HandleKey(EditorKey.D, KeyModifiers.Control, model);
        await Task.Yield();
        Check.Equal(new SelectionState(2, 3), model.Selection, "VS Code's Ctrl+D selects the word");
        model.Select(0, 1);
        host.HandleKey(EditorKey.L, KeyModifiers.Control | KeyModifiers.Shift, model);
        await Task.Yield();
        Check.Equal(3, model.SelectionCount, "VS Code's Ctrl+Shift+L selects every 'x'");
        host.HandleKey(EditorKey.Escape, KeyModifiers.None, model);
        Check.Equal(1, model.SelectionCount, "Escape: one caret");
        model.Select(0, 0);
        host.HandleKey(EditorKey.Down, KeyModifiers.Control | KeyModifiers.Alt, model);
        await Task.Yield();
        Check.Equal(2, model.SelectionCount, "Ctrl+Alt+Down adds a caret below");
        model.Select(0, 0);
        host.HandleKey(EditorKey.Down, KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt, model);
        await Task.Yield();
        Check.Equal(2, model.SelectionCount, "VS Code's Ctrl+Shift+Alt+Down column-selects");

        Check.True(fixture.Shell.ApplySetting(SettingLayer.User, "CRAFT_KEYMAP", StandardShortcuts.VisualStudio) is null, "the Visual Studio keymap");
        model.Select(2, 3);
        host.HandleKey(EditorKey.Period, KeyModifiers.Shift | KeyModifiers.Alt, model);
        await Task.Yield();
        Check.Equal(2, model.SelectionCount, "Visual Studio's Shift+Alt+. adds the next occurrence");
        model.Select(0, 0);
        host.HandleKey(EditorKey.Down, KeyModifiers.Shift | KeyModifiers.Alt, model);
        await Task.Yield();
        Check.Equal(2, model.SelectionCount, "Visual Studio's Shift+Alt+Down column-selects");
        model.Select(0, 0);
        host.HandleKey(EditorKey.D, KeyModifiers.Control, model);
        await Task.Yield();
        Check.Equal("x a x\nx a x\nb a\nx", document.Text, "its Ctrl+D still duplicates the line");
        Check.True(EditorCommands.Find(StandardShortcuts.FoldAll) is not null && EditorCommands.Find(StandardShortcuts.ToggleFold) is not null, "the fold commands are registered");
        var rows = new KeyboardModel(fixture.Shell).Rows();
        Check.True(rows.Any(r => r.CommandId == StandardShortcuts.AddNextOccurrence && r.Keys.Any(k => k.Text == "Shift+Alt+.")), "the Keyboard page shows the keymap's keys");
    }

    [Test]
    public static void AKeystrokeAtAHundredCaretsIsMeasured()
    {
        var text = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            text.Append("    int value").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" = 1;\n");
        }

        const int keys = 400;
        // Pass 1, not measured: the changes the editor makes, recorded, to replay straight into a document (its share).
        var recorded = new List<(TextEdit[] Edits, EditKind Kind)>();
        var (recorder, recorderDocument) = RealEditors.Make(text.ToString(), language: new QuoteLanguage());
        using (recorder)
        {
            SetCarets(recorder, recorderDocument);
            recorderDocument.Buffer.Changed += (_, e) => recorded.Add(([.. e.Edits], recorded.Count < keys ? EditKind.Typing : EditKind.Deleting));
            Keystrokes(recorder, keys, null, null);
        }

        // Pass 2: the document alone, the same changes: before (each edit on its own path, the batch off) and after (the
        // X restart's batched build, the default), each with its pieces after the 400 typed keys.
        var (beforeTimes, beforeBytes, beforePieces, beforeAdded) = Replay(new CraftOptions { NewLine = "\n", PieceBatchMinEdits = 1_000_000 });
        var (documentTimes, documentBytes, afterPieces, afterAdded) = Replay(new CraftOptions { NewLine = "\n" });

        // Pass 3: the editor, the keystrokes as the user types them (the document's share included), then each with its paint.
        var (model, document) = RealEditors.Make(text.ToString(), language: new QuoteLanguage());
        using var _ = model;
        SetCarets(model, document);
        var keyTimes = new double[2 * keys];
        long before = GC.GetAllocatedBytesForCurrentThread();
        Keystrokes(model, keys, keyTimes, null);
        long editorBytes = (GC.GetAllocatedBytesForCurrentThread() - before) / (2 * keys);
        using var renderer = new EditorRenderer();
        var canvas = new CountingCanvas();
        var paintTimes = new double[2 * keys];
        Keystrokes(model, keys, null, i =>
        {
            long start = Stopwatch.GetTimestamp();
            renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
            paintTimes[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
        });

        Array.Sort(keyTimes);
        Array.Sort(paintTimes);
        long ownBytes = Math.Max(0, editorBytes - documentBytes);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"    measured: a keystroke at 100 carets (typing and Backspace): median {Median(keyTimes):F1} us, p99 {P99(keyTimes):F1} us, {editorBytes:N0} bytes; of which the document's change {Median(documentTimes):F1} us and {documentBytes:N0} bytes, the editor's own about {ownBytes:N0} bytes; its paint median {Median(paintTimes):F1} us, p99 {P99(paintTimes):F1} us"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"    measured: the document's change at 100 carets, before (edit by edit): median {Median(beforeTimes):F1} us, p99 {P99(beforeTimes):F1} us, {beforeBytes:N0} bytes, {beforePieces:N0} pieces after 400 keys, {beforeAdded:N0} characters added; after (batched): median {Median(documentTimes):F1} us, p99 {P99(documentTimes):F1} us, {documentBytes:N0} bytes, {afterPieces:N0} pieces, {afterAdded:N0} characters added"));
        Check.True(afterPieces <= (100 * ((keys + 63) / 64 + 1)) + 101, $"the batch keeps a piece a caret per 64 typed characters ({afterPieces:N0} pieces after {keys} keys; edit by edit {beforePieces:N0})");
        Check.True(documentBytes < beforeBytes, $"the batched change allocates less than the edit-by-edit one ({documentBytes:N0} against {beforeBytes:N0} bytes)");
        Check.Equal(100, model.SelectionCount, "still 100 carets");
        Check.True(renderer.CaretsDrawn == 21, $"the visible lines' carets drawn ({renderer.CaretsDrawn})");
        Check.True(Median(keyTimes) < 5_000, $"a keystroke at 100 carets stays well under a frame ({Median(keyTimes):F1} us)");
        Check.True(ownBytes < 4_096, $"the editor's own work allocates next to nothing per keystroke ({ownBytes:N0} bytes over the document's)");

        static void SetCarets(EditorModel model, TextDocument document)
        {
            var snapshot = document.Buffer.Current;
            var carets = new SelectionState[100];
            for (int i = 0; i < 100; i++)
            {
                carets[i] = SelectionState.At(snapshot.GetLine(i).End - 1);
            }

            model.SetSelections(carets, 0);
        }

        // `keys` characters typed, then as many Backspaces; each timed into `times`, `after` run after each.
        static void Keystrokes(EditorModel model, int keys, double[]? times, Action<int>? after)
        {
            for (int i = 0; i < 2 * keys; i++)
            {
                long start = Stopwatch.GetTimestamp();
                if (i < keys)
                {
                    model.OnText("x");
                }
                else
                {
                    model.OnKey(EditorKey.Backspace, None);
                }

                if (times is not null)
                {
                    times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                }

                after?.Invoke(i);
            }
        }

        // The recorded changes replayed into a document of `options`: each change's time (sorted), the bytes a change, the
        // pieces after the typed keys and the characters the add buffer took.
        (double[] Times, long Bytes, int Pieces, long Added) Replay(CraftOptions options)
        {
            var replay = TextDocument.Create(text.ToString(), options);
            var dummy = new SelectionState[100];
            var times = new double[recorded.Count];
            int pieces = 0;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < recorded.Count; i++)
            {
                long start = Stopwatch.GetTimestamp();
                replay.Edit(recorded[i].Edits, recorded[i].Kind, dummy, 0, dummy, 0);
                times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                if (i == keys - 1)
                {
                    long counting = GC.GetAllocatedBytesForCurrentThread();
                    pieces = Craft.Core.Text.PieceTableBuffer.PieceCount(replay.Buffer.Current);
                    allocated += GC.GetAllocatedBytesForCurrentThread() - counting;
                }
            }

            long bytes = (GC.GetAllocatedBytesForCurrentThread() - allocated) / recorded.Count;
            Array.Sort(times);
            return (times, bytes, pieces, ((Craft.Core.Text.PieceTableBuffer)replay.Buffer).AddedCharacters);
        }

        static double Median(double[] sorted) => sorted[sorted.Length / 2];

        static double P99(double[] sorted) => sorted[sorted.Length * 99 / 100];
    }
}

/// <summary>Phase X: code folding (the contract, the fold map, the commands, the providers, the paint of a folded file).</summary>
[TestGroup("Folding")]
public static class FoldingTests
{
    [Test]
    public static void TheFoldMapAnswersAsABruteForceModelDoes()
    {
        var random = new Random(17);
        for (int round = 0; round < 50; round++)
        {
            int lines = random.Next(5, 300);
            var map = new FoldMap();
            map.SetLineCount(lines);
            var folded = new List<FoldingRange>();
            for (int f = 0; f < random.Next(0, 12); f++)
            {
                int start = random.Next(0, lines - 1);
                int end = random.Next(start + 1, Math.Min(lines, start + 40));
                if (map.Fold(new FoldingRange(start, end)))
                {
                    folded.RemoveAll(x => x.StartLine == start);
                    folded.Add(new FoldingRange(start, end));
                }
            }

            if (folded.Count > 0 && random.Next(3) == 0)
            {
                var gone = folded[random.Next(folded.Count)];
                map.Unfold(gone.StartLine);
                folded.Remove(gone);
            }

            var hidden = new bool[lines];
            foreach (var f in folded)
            {
                for (int l = f.StartLine + 1; l <= f.EndLine; l++)
                {
                    hidden[l] = true;
                }
            }

            var shown = Enumerable.Range(0, lines).Where(l => !hidden[l]).ToList();
            Check.Equal(shown.Count, map.RowCount, $"round {round}: the rows");
            for (int row = 0; row < shown.Count; row++)
            {
                Check.Equal(shown[row], map.LineAt(row), $"round {round}: the line of row {row}");
                Check.Equal(row, map.RowOf(shown[row]), $"round {round}: the row of line {shown[row]}");
            }

            for (int l = 0; l < lines; l++)
            {
                Check.Equal(hidden[l], map.IsHidden(l), $"round {round}: line {l} hidden");
                int next = shown.FirstOrDefault(x => x > l, lines);
                Check.Equal(next, map.NextShown(l), $"round {round}: the line shown after {l}");
                if (hidden[l])
                {
                    Check.Equal(map.RowOf(shown.Last(x => x < l)), map.RowOf(l), $"round {round}: a hidden line's row is its header's");
                }
            }
        }

        var nested = new FoldMap();
        nested.SetLineCount(20);
        nested.Fold(new FoldingRange(2, 10));
        nested.Fold(new FoldingRange(4, 6));
        nested.Unfold(2);
        Check.True(nested.IsHidden(5) && !nested.IsHidden(3) && nested.HiddenLines == 2, "unfolding the outer fold keeps the inner one folded");
    }

    [Test]
    public static async Task FoldCommandsHideLinesAndTheCaretSearchAndGoToLineUnfold()
    {
        var provider = new BraceFolding("quotes");
        FoldingRangeProviders.Register(provider);
        try
        {
            string text = "class A {\n  void M() {\n    x;\n  }\n  y;\n}\nz";
            var (model, document) = RealEditors.Make(text, language: new QuoteLanguage());
            using var _ = model;
            await model.Folding.RefreshAsync();
            Check.True(model.Folding.IsAvailable && provider.Calls == 1, "the provider was asked off the UI thread, once for the version");
            Check.SequenceEqual([new FoldingRange(0, 5), new FoldingRange(1, 3)], model.Folding.Ranges.ToArray(), "its ranges, by header line");
            Check.Equal((1 + 2 + 2) * 10f, model.GutterWidth, "the gutter has its fold column (one digit, the padding, the chevrons)");

            model.Select(25, 25);
            Check.True(model.Folding.Fold(model.CaretLine), "Ctrl+Shift+[ folds the innermost range at the caret");
            Check.Equal(5, model.RowCount, "two lines hidden");
            Check.Equal((5 + 3) * 20f, model.ContentHeight, "the content's height follows the rows");
            Check.Equal(document.Buffer.Current.GetLine(1).End, model.Selection.Caret, "the caret the fold hid went to its header's end");
            model.OnKey(EditorKey.Down, KeyModifiers.None);
            Check.Equal(4, model.CaretLine, "Down steps over the folded lines");

            using var renderer = new EditorRenderer();
            var canvas = new RecordingCanvas();
            renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
            var numbers = canvas.Texts.Where(t => t.X < model.GutterWidth).Select(t => t.Text).ToList();
            Check.SequenceEqual(["1", "2", "5", "6", "7"], numbers, "the folded lines are not drawn; the numbers stay the lines'");
            Check.True(canvas.Texts.Any(t => t.Text == "..."), "the folded header shows its marker");
            Check.Equal(5, renderer.LinesDrawn, "five rows drawn");
            Check.True(canvas.Texts.First(t => t.Text == "5").Top == 40, "line 5 is drawn on the third row");

            model.GoToLine(3);
            Check.True(!model.Folding.Map.HasHidden, "go to line into the fold unfolds it");
            model.Folding.FoldAll();
            Check.Equal(2, model.RowCount, "fold all: the outer range hides the rest of the class");
            model.Find.Query = "x;";
            model.Select(0, 0);
            model.Find.FindNext();
            Check.True(!model.Folding.Map.IsHidden(2), "a search that lands in a fold unfolds what hides it");
            model.Folding.FoldAll();
            model.Folding.UnfoldAll();
            Check.Equal(7, model.RowCount, "unfold all");

            // The gutter's chevron: a click on the fold column of line 2 folds it, another unfolds it.
            float chevronX = model.GutterWidth - 10;
            model.PointerPressed(chevronX, 21, KeyModifiers.None);
            model.PointerReleased();
            Check.True(model.Folding.IsFolded(1), "a click on the chevron folds");
            model.PointerPressed(chevronX, 21, KeyModifiers.None);
            model.PointerReleased();
            Check.True(!model.Folding.IsFolded(1), "and unfolds");
        }
        finally
        {
            FoldingRangeProviders.Unregister(provider.Name);
        }
    }

    [Test]
    public static async Task FoldsFollowEditsAndUnfoldWhenAnEditReachesTheirLines()
    {
        var provider = new BraceFolding("quotes");
        FoldingRangeProviders.Register(provider);
        try
        {
            var (model, document) = RealEditors.Make("a\nb {\n c\n d\n}\ne", language: new QuoteLanguage());
            using var _ = model;
            await model.Folding.RefreshAsync();
            model.Folding.Toggle(1);
            Check.True(model.Folding.IsFolded(1) && model.RowCount == 3, "folded");
            model.Select(0, 0);
            model.OnText("x");
            Check.True(model.Folding.IsFolded(1), "typing on another line keeps the fold");
            model.OnKey(EditorKey.Enter, KeyModifiers.None);
            Check.True(model.Folding.IsFolded(2) && model.RowCount == 4, "a line added above moves the fold down");
            Check.SequenceEqual([new FoldingRange(2, 5)], model.Folding.Ranges.ToArray(), "and its range, until the provider answers again");
            model.Select(document.Buffer.Current.GetLine(2).End, document.Buffer.Current.GetLine(2).End);
            model.OnText("y");
            Check.True(model.Folding.IsFolded(2), "typing on the header keeps it folded");
            model.OnKey(EditorKey.Delete, KeyModifiers.None);
            Check.True(!model.Folding.Map.HasHidden, "deleting into the hidden lines unfolds them (an edit never happens out of sight)");
            await model.Folding.RefreshAsync();
            Check.True(provider.Calls == 2 && model.Folding.RangesVersion == document.Buffer.Current.Version, "asked again for the new version");
        }
        finally
        {
            FoldingRangeProviders.Unregister(provider.Name);
        }
    }

    [Test]
    public static void TheCSharpMarkupAndJsonProvidersGiveTheirRanges()
    {
        string code = "using System;\nusing System.IO;\n\nnamespace N\n{\n    /// <summary>\n    /// Doc.\n    /// </summary>\n    [Serializable]\n    class A\n    {\n        #region Fields\n        int x;\n        #endregion\n\n        void M()\n        {\n            if (x > 0)\n            {\n                x = 1;\n            }\n            var list = new[]\n            {\n                1,\n            };\n        }\n\n        // one\n        // two\n        int P { get; set; }\n    }\n}\n";
        var buffer = new StringBuffer(code);
        var snapshot = buffer.Current;
        var root = CSharpSyntaxTree.ParseText(code).GetRoot();
        var ranges = CSharpFolding.Ranges(root, snapshot, CancellationToken.None);
        void Has(int start, int end, FoldingRangeKind kind, string what) =>
            Check.True(ranges.Contains(new FoldingRange(start, end, kind)), $"{what}: ({start}, {end}, {kind}) in [{string.Join(", ", ranges)}]");
        Has(0, 1, FoldingRangeKind.Imports, "the usings");
        Has(3, 31, FoldingRangeKind.Region, "the namespace");
        Has(5, 7, FoldingRangeKind.Comment, "the doc comment");
        Has(9, 30, FoldingRangeKind.Region, "the class, from its line after the attribute");
        Has(11, 13, FoldingRangeKind.Marked, "#region to #endregion");
        Has(15, 25, FoldingRangeKind.Region, "the method");
        Has(17, 20, FoldingRangeKind.Region, "the if's block");
        Has(21, 24, FoldingRangeKind.Region, "the initializer");
        Has(27, 28, FoldingRangeKind.Comment, "the line comments");

        string xml = "<Project>\n  <!-- a\n  b -->\n  <PropertyGroup>\n    <A>1</A>\n  </PropertyGroup>\n  <Item\n    Include=\"x\" />\n</Project>\n";
        var markup = MarkupFolding.Ranges(new StringBuffer(xml).Current);
        Check.SequenceEqual([new FoldingRange(1, 2, FoldingRangeKind.Comment), new FoldingRange(3, 5), new FoldingRange(6, 7), new FoldingRange(0, 8)], markup, "XML: the comment, the elements and the long tag");

        string json = "{\n  \"a\": [\n    1,\n    \"]\"\n  ],\n  /* x\n  y */\n  \"b\": {}\n}\n";
        var objects = JsonFolding.Ranges(new StringBuffer(json).Current);
        Check.SequenceEqual([new FoldingRange(1, 4), new FoldingRange(5, 6, FoldingRangeKind.Comment), new FoldingRange(0, 8)], objects, "JSON: the array (a ']' in a string skipped), the comment, the object; '{}' on one line not");
    }

    [Test]
    public static async Task TheCSharpPluginsProviderFoldsAnOpenDocumentOffTheUiThread()
    {
        CSharpLanguage.Register();
        try
        {
            var (model, _) = RealEditors.Make("class A\n{\n    void M()\n    {\n    }\n}\n", language: EditorLanguages.Get(CSharpLanguage.RegularName));
            using var _m = model;
            await model.Folding.RefreshAsync();
            Check.SequenceEqual([new FoldingRange(0, 5), new FoldingRange(2, 4)], model.Folding.Ranges.ToArray(), "the class and the method, from Roslyn's tree");
            Check.True(model.Folding.LastError is null, "no provider failed");
        }
        finally
        {
            CSharpLanguage.Unregister();
        }
    }

    [Test]
    public static void APaintOfAFolded100000LineFileDrawsAndClassifiesOnlyTheRowsOnScreen()
    {
        var provider = new BraceFolding("quotes");
        FoldingRangeProviders.Register(provider);
        try
        {
            var text = new StringBuilder();
            for (int block = 0; block < 10_000; block++)
            {
                text.Append("block ").Append(block.ToString(CultureInfo.InvariantCulture)).Append(" {\n");
                for (int i = 0; i < 8; i++)
                {
                    text.Append("    \"s\" line\n");
                }

                text.Append("}\n");
            }

            var language = new QuoteLanguage();
            var (model, document) = RealEditors.Make(text.ToString(), language: language);
            using var _ = model;
            Check.Equal(100_001, document.Buffer.Current.LineCount, "100,000 lines");
            model.Folding.SetRanges(BraceFolding.Braces(document.Buffer.Current));
            long start = Stopwatch.GetTimestamp();
            int folded = model.Folding.FoldAll();
            double foldMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Check.Equal(10_000, folded, "10,000 folds");
            Check.Equal(10_001, model.RowCount, "each block shows its header line");
            using var renderer = new EditorRenderer();
            var canvas = new CountingCanvas();
            float scrollY = 5_000 * 20;
            renderer.Draw(canvas, model, 0, scrollY, 800, 400, caretVisible: true);
            Check.Equal(21, renderer.LinesDrawn, "21 rows drawn");
            Check.Equal(new LineRange(50_000, 50_200), renderer.LastVisible, "the headers of blocks 5,000 to 5,020");
            language.CharactersClassified = 0;
            const int frames = 1000;
            var times = new double[frames];
            for (int i = 0; i < frames; i++)
            {
                start = Stopwatch.GetTimestamp();
                renderer.Draw(canvas, model, 0, scrollY + ((i & 1) * 20), 800, 400, caretVisible: (i & 2) == 0);
                times[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }

            Array.Sort(times);
            long perFrame = language.CharactersClassified / frames;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"    measured: a folded 100,000-line file (10,000 folds): fold all {foldMs:F1} ms; a paint median {times[frames / 2]:F1} us, p99 {times[frames * 99 / 100]:F1} us; {perFrame} characters classified a frame"));
            Check.True(perFrame < 22 * 20, $"only the rows on screen are classified ({perFrame} characters a frame)");
            Check.True(times[frames / 2] < 5_000, $"a paint stays well under a frame ({times[frames / 2]:F1} us)");
        }
        finally
        {
            FoldingRangeProviders.Unregister(provider.Name);
        }
    }
}

/// <summary>Phase X: indentation guides, the matching bracket and current word highlights (each an option), auto-indent and auto-close that never fight.</summary>
[TestGroup("Surface")]
public static class EditingSurfaceTests
{
    [Test]
    public static void GuidesWordAndBracketHighlightsDrawAndEachSwitchesOff()
    {
        string text = "class A\n{\n    void M()\n    {\n\n        foo(foo);\n    }\n}";
        var (model, _, _) = Editors.Make(text);
        model.Select(text.IndexOf("foo(", StringComparison.Ordinal) + 1, text.IndexOf("foo(", StringComparison.Ordinal) + 1);
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        var colors = model.Theme.Colors;
        // Line 2 (4 spaces): 1 guide; line 3: 1; the blank line 4: min(4, 8) -> 1; line 5 (8): 2; line 6: 1.
        Check.Equal(6, renderer.GuidesDrawn, "a guide at each indent level, through the blank line");
        Check.True(canvas.Calls.Count(c => c.Kind == "line" && c.Argb == colors.IndentGuide) == 6, "drawn in the theme's guide colour");
        Check.Equal(2, renderer.WordHighlightsDrawn, "both places of 'foo' highlighted");
        Check.True(canvas.Calls.Any(c => c.Kind == "fill" && c.Argb == colors.WordHighlight), "in the theme's word colour");

        model.Select(text.IndexOf("(foo)", StringComparison.Ordinal), text.IndexOf("(foo)", StringComparison.Ordinal));
        canvas.Clear();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        Check.Equal(2, canvas.Calls.Count(c => c.Argb == colors.BracketMatch), "the bracket and its partner");

        var (off, _, _) = Editors.Make(text, new CraftOptions { IndentGuides = false, WordHighlight = false, BracketHighlight = false });
        off.Select(text.IndexOf("(foo)", StringComparison.Ordinal), text.IndexOf("(foo)", StringComparison.Ordinal));
        canvas.Clear();
        renderer.Draw(canvas, off, 0, 0, 800, 400, caretVisible: true);
        Check.True(renderer.GuidesDrawn == 0 && renderer.WordHighlightsDrawn == 0 && !canvas.Calls.Any(c => c.Argb == colors.BracketMatch), "each switched off draws nothing");
        Check.True(CraftOptions.Variables.Any(v => v.Name == "CRAFT_INDENT_GUIDES" && v.Shape.Kind == SettingKind.Toggle)
            && CraftOptions.Variables.Any(v => v.Name == "CRAFT_WORD_HIGHLIGHT") && CraftOptions.Variables.Any(v => v.Name == "CRAFT_BRACKET_HIGHLIGHT"), "the options are on the settings page");
    }

    [Test]
    public static void AutoCloseAndAutoIndentNeverFightTheUser()
    {
        var on = new CraftOptions { AutoCloseBrackets = true };
        var (pair, pairDocument, _) = Editors.MakeMarked("x|", on);
        pair.OnText("(");
        pair.OnKey(EditorKey.Backspace, KeyModifiers.None);
        Check.Equal("x|", Editors.Marked(pair, pairDocument), "Backspace in an empty auto-inserted pair deletes both");

        var (typed, typedDocument, _) = Editors.MakeMarked("|", on);
        typed.OnText("(");
        typed.OnText("a");
        typed.OnText("b");
        typed.OnKey(EditorKey.Backspace, KeyModifiers.None);
        typed.OnText(")");
        Check.Equal("(a)|", Editors.Marked(typed, typedDocument), "after a Backspace inside the pair, ')' still steps over the close");

        var (manual, manualDocument, _) = Editors.MakeMarked("(|)", on);
        manual.OnKey(EditorKey.Backspace, KeyModifiers.None);
        Check.Equal("|)", Editors.Marked(manual, manualDocument), "a pair the user typed is not deleted whole");

        var (paste, pasteDocument, _) = Editors.MakeMarked("|", on);
        paste.PasteText("(");
        Check.Equal("(|", Editors.Marked(paste, pasteDocument), "a paste never auto-closes");

        var (block, blockDocument, _) = Editors.MakeMarked("    if (x) {|", on);
        block.OnKey(EditorKey.Enter, KeyModifiers.None);
        block.OnText("y;");
        block.OnKey(EditorKey.Enter, KeyModifiers.None);
        block.OnText("}");
        Check.Equal("    if (x) {\n        y;\n    }|", Editors.Marked(block, blockDocument), "a '}' typed on a blank line takes its opening line's indent");

        var (inner, innerDocument, _) = Editors.MakeMarked("    {|}", on);
        inner.OnKey(EditorKey.Enter, KeyModifiers.None);
        Check.Equal("    {\n        |\n    }", Editors.Marked(inner, innerDocument), "Enter between braces (unchanged)");
        inner.OnText("}");
        Check.Equal("    {\n    }|\n    }", Editors.Marked(inner, innerDocument), "a '}' on the blank inner line outdents to its opener");
    }
}

/// <summary>A canvas that only counts (the measures time the editor, not a recording).</summary>
internal sealed class CountingCanvas : IEditorCanvas
{
    public long Calls { get; private set; }

    public void FillRect(float x, float y, float width, float height, uint argb) => Calls++;

    public void DrawLine(float x1, float y1, float x2, float y2, uint argb, float thickness) => Calls++;

    public void DrawText(string text, float x, float top, in TextStyle style) => Calls++;
}
