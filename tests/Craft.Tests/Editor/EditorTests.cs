// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests.Editor;

/// <summary>The editor without a UI (Craft.Editor), driven headless with fixed metrics (cells 10 wide, lines 20 high).</summary>
[TestGroup("Editor")]
public static class EditorTests
{
    private const KeyModifiers None = KeyModifiers.None;
    private const KeyModifiers Shift = KeyModifiers.Shift;
    private const KeyModifiers Ctrl = KeyModifiers.Control;

    [Test]
    public static void CaretMovesByCharacterAcrossLinesAndTabs()
    {
        var (model, _, _) = Editors.Make("ab\n\tc");
        model.OnKey(EditorKey.Right, None);
        model.OnKey(EditorKey.Right, None);
        Check.Equal(2, model.Selection.Caret, "two Rights");
        model.OnKey(EditorKey.Right, None);
        Check.Equal(3, model.Selection.Caret, "Right at a line's end goes to the next line's start");
        model.OnKey(EditorKey.Right, None);
        Check.Equal(4, model.CaretColumn, "a tab spans to the next tab stop (column 4)");
        model.OnKey(EditorKey.Left, None);
        model.OnKey(EditorKey.Left, None);
        Check.Equal(2, model.Selection.Caret, "Left at a line's start goes to the previous line's end");

        var (vertical, _, _) = Editors.Make("abcdef\nab\nabcdef");
        vertical.Select(5, 5);
        vertical.OnKey(EditorKey.Down, None);
        Check.Equal(9, vertical.Selection.Caret, "Down onto a shorter line stops at its end");
        vertical.OnKey(EditorKey.Down, None);
        Check.Equal(15, vertical.Selection.Caret, "the next Down returns to the desired column 5");
        vertical.OnKey(EditorKey.Up, None);
        vertical.OnKey(EditorKey.Up, None);
        Check.Equal(5, vertical.Selection.Caret, "Up twice comes back to column 5");

        var (tabs, _, _) = Editors.Make("\tx\nabcdefgh");
        tabs.Select(1, 1);
        tabs.OnKey(EditorKey.Down, None);
        Check.Equal(7, tabs.Selection.Caret, "Down after a tab keeps its visual column (4)");
    }

    [Test]
    public static void WordMovesStopAtWordAndPunctuationEdges()
    {
        var (model, _, _) = Editors.Make("foo.bar  baz");
        int[] right = [3, 4, 9, 12];
        foreach (int expected in right)
        {
            model.OnKey(EditorKey.Right, Ctrl);
            Check.Equal(expected, model.Selection.Caret, "Ctrl+Right");
        }

        int[] left = [9, 4, 3, 0];
        foreach (int expected in left)
        {
            model.OnKey(EditorKey.Left, Ctrl);
            Check.Equal(expected, model.Selection.Caret, "Ctrl+Left");
        }
    }

    [Test]
    public static void ShiftExtendsAndArrowsCollapse()
    {
        var (model, _, _) = Editors.Make("hello world");
        model.OnKey(EditorKey.Right, Shift);
        model.OnKey(EditorKey.Right, Shift);
        Check.Equal(new SelectionState(0, 2), model.Selection, "Shift+Right twice");
        model.OnKey(EditorKey.Right, Shift | Ctrl);
        Check.Equal(new SelectionState(0, 6), model.Selection, "Shift+Ctrl+Right extends by a word");
        model.OnKey(EditorKey.Left, None);
        Check.Equal(SelectionState.At(0), model.Selection, "Left collapses a selection to its start");
        model.OnKey(EditorKey.A, Ctrl);
        Check.Equal(new SelectionState(0, 11), model.Selection, "Ctrl+A");
        model.OnKey(EditorKey.Right, None);
        Check.Equal(SelectionState.At(11), model.Selection, "Right collapses a selection to its end");
    }

    [Test]
    public static void HomeIsSmartAndEndGoesToTheLineEnd()
    {
        var (model, _, _) = Editors.Make("    code\nx");
        model.Select(8, 8);
        model.OnKey(EditorKey.Home, None);
        Check.Equal(4, model.Selection.Caret, "Home goes to the first non-blank");
        model.OnKey(EditorKey.Home, None);
        Check.Equal(0, model.Selection.Caret, "Home again goes to column 0");
        model.OnKey(EditorKey.Home, None);
        Check.Equal(4, model.Selection.Caret, "and back to the first non-blank");
        model.OnKey(EditorKey.End, None);
        Check.Equal(8, model.Selection.Caret, "End");
        model.OnKey(EditorKey.End, Ctrl);
        Check.Equal(10, model.Selection.Caret, "Ctrl+End");
        model.OnKey(EditorKey.Home, Ctrl);
        Check.Equal(0, model.Selection.Caret, "Ctrl+Home");
    }

    [Test]
    public static void TypingUndoAndRedoRestoreTheSelection()
    {
        var (model, document, _) = Editors.MakeMarked("ab|");
        model.OnText("c");
        model.OnText("d");
        Check.Equal("abcd|", Editors.Marked(model, document), "typed");
        model.OnKey(EditorKey.Z, Ctrl);
        Check.Equal("abc|", Editors.Marked(model, document), "undo");
        model.OnKey(EditorKey.Z, Ctrl);
        Check.Equal("ab|", Editors.Marked(model, document), "undo again");
        model.OnKey(EditorKey.Y, Ctrl);
        Check.Equal("abc|", Editors.Marked(model, document), "Ctrl+Y redoes");
        model.OnKey(EditorKey.Z, Ctrl | Shift);
        Check.Equal("abcd|", Editors.Marked(model, document), "Ctrl+Shift+Z redoes");
        Check.True(document.Kinds.All(k => k == EditKind.Typing), "typing is EditKind.Typing");

        var (selected, selectedDocument, _) = Editors.MakeMarked("[ab]c");
        selected.OnText("x");
        Check.Equal("x|c", Editors.Marked(selected, selectedDocument), "typing replaces the selection");
        selected.OnKey(EditorKey.Z, Ctrl);
        Check.Equal("[ab]c", Editors.Marked(selected, selectedDocument), "undo restores the selection it came with");
    }

    [Test]
    public static void EnterCopiesIndentationAndOpensBrackets()
    {
        var (plain, plainDocument, _) = Editors.MakeMarked("    if (x)|");
        plain.OnKey(EditorKey.Enter, None);
        Check.Equal("    if (x)\n    |", Editors.Marked(plain, plainDocument), "Enter copies the line's indentation");

        var (open, openDocument, _) = Editors.MakeMarked("    {|");
        open.OnKey(EditorKey.Enter, None);
        Check.Equal("    {\n        |", Editors.Marked(open, openDocument), "one more level after an opening bracket");

        var (pair, pairDocument, _) = Editors.MakeMarked("    {|}");
        pair.OnKey(EditorKey.Enter, None);
        Check.Equal("    {\n        |\n    }", Editors.Marked(pair, pairDocument), "Enter between {} puts the close on its own line");

        var (crlf, crlfDocument, _) = Editors.MakeMarked("a\r\n{|}");
        crlf.OnKey(EditorKey.Enter, None);
        Check.Equal("a\r\n{\r\n    |\r\n}", Editors.Marked(crlf, crlfDocument), "Enter inserts the document's own line break");

        var (tabs, tabsDocument, _) = Editors.MakeMarked("\t(|", new CraftOptions { InsertSpaces = false });
        tabs.OnKey(EditorKey.Enter, None);
        Check.Equal("\t(\n\t\t|", Editors.Marked(tabs, tabsDocument), "a tab is the level when InsertSpaces is off");
    }

    [Test]
    public static void TabIndentsAndShiftTabOutdentsLines()
    {
        var (one, oneDocument, _) = Editors.MakeMarked("|x");
        one.OnKey(EditorKey.Tab, None);
        Check.Equal("    |x", Editors.Marked(one, oneDocument), "Tab inserts spaces to the next tab stop");
        var (mid, midDocument, _) = Editors.MakeMarked("ab|");
        mid.OnKey(EditorKey.Tab, None);
        Check.Equal("ab  |", Editors.Marked(mid, midDocument), "from column 2, two spaces reach the tab stop");

        var (lines, linesDocument, _) = Editors.MakeMarked("[a\nb\nc]");
        lines.OnKey(EditorKey.Tab, None);
        Check.Equal("[    a\n    b\n    c]", Editors.Marked(lines, linesDocument), "Tab over several lines indents each");
        Check.Equal(EditKind.Command, linesDocument.Kinds[^1], "an indent is a command");
        lines.OnKey(EditorKey.Tab, Shift);
        Check.Equal("[a\nb\nc]", Editors.Marked(lines, linesDocument), "Shift+Tab outdents them back");
        lines.OnKey(EditorKey.Z, Ctrl);
        Check.Equal("[    a\n    b\n    c]", Editors.Marked(lines, linesDocument), "undo of the outdent is one step");

        var (excluded, excludedDocument, _) = Editors.MakeMarked("[a\n]b");
        excluded.OnKey(EditorKey.Tab, None);
        Check.Equal("[    a\n]b", Editors.Marked(excluded, excludedDocument), "a selection ending at column 0 leaves that line alone");

        var (caret, caretDocument, _) = Editors.MakeMarked("      x|");
        caret.OnKey(EditorKey.Tab, Shift);
        Check.Equal("    x|", Editors.Marked(caret, caretDocument), "Shift+Tab with no selection outdents to the previous tab stop");
    }

    [Test]
    public static void BackspaceAndDeleteByWordAndByTabStop()
    {
        var (word, wordDocument, _) = Editors.MakeMarked("foo bar|");
        word.OnKey(EditorKey.Backspace, Ctrl);
        Check.Equal("foo |", Editors.Marked(word, wordDocument), "Ctrl+Backspace deletes the word before");

        var (stop, stopDocument, _) = Editors.MakeMarked("      |x");
        stop.OnKey(EditorKey.Backspace, None);
        Check.Equal("    |x", Editors.Marked(stop, stopDocument), "Backspace in leading spaces goes back to the previous tab stop");
        stop.OnKey(EditorKey.Backspace, None);
        Check.Equal("|x", Editors.Marked(stop, stopDocument), "and to column 0");
        Check.True(stopDocument.Kinds.All(k => k == EditKind.Deleting), "deleting is EditKind.Deleting");

        var (one, oneDocument, _) = Editors.MakeMarked("ab|");
        one.OnKey(EditorKey.Backspace, None);
        Check.Equal("a|", Editors.Marked(one, oneDocument), "Backspace deletes one character");

        var (forward, forwardDocument, _) = Editors.MakeMarked("|foo bar");
        forward.OnKey(EditorKey.Delete, Ctrl);
        Check.Equal("|bar", Editors.Marked(forward, forwardDocument), "Ctrl+Delete deletes to the next word");

        var (join, joinDocument, _) = Editors.MakeMarked("a|\r\nb");
        join.OnKey(EditorKey.Delete, None);
        Check.Equal("a|b", Editors.Marked(join, joinDocument), "Delete at a line's end removes the whole \\r\\n");
        var (back, backDocument, _) = Editors.MakeMarked("a\r\n|b");
        back.OnKey(EditorKey.Backspace, None);
        Check.Equal("a|b", Editors.Marked(back, backDocument), "Backspace at a line's start removes the whole \\r\\n");
    }

    [Test]
    public static void CopyCutAndPasteGoThroughTheClipboard()
    {
        var (model, document, clipboard) = Editors.MakeMarked("[ab]c");
        model.OnKey(EditorKey.C, Ctrl);
        Check.Equal("ab", clipboard.Text, "Ctrl+C copies the selection");
        Check.Equal("abc", document.Text, "copy leaves the text");

        var (line, lineDocument, lineClipboard) = Editors.MakeMarked("x\n|yz\nw");
        line.OnKey(EditorKey.C, Ctrl);
        Check.Equal("yz\n", lineClipboard.Text, "copy with no selection copies the line");
        line.OnKey(EditorKey.X, Ctrl);
        Check.Equal("x\n|w", Editors.Marked(line, lineDocument), "cut with no selection cuts the line");
        Check.Equal(EditKind.Command, lineDocument.Kinds[^1], "a cut is a command");

        var (paste, pasteDocument, pasteClipboard) = Editors.MakeMarked("a|b");
        pasteClipboard.Text = "Q\r\nR";
        paste.OnKey(EditorKey.V, Ctrl);
        Check.Equal("aQ\nR|b", Editors.Marked(paste, pasteDocument), "paste inserts at the caret in the document's line breaks");
        Check.Equal(EditKind.Paste, pasteDocument.Kinds[^1], "a paste is EditKind.Paste");
    }

    [Test]
    public static void FindNextPreviousAndWrap()
    {
        var (model, _, _) = Editors.Make("foo bar foo\nfoo");
        model.Find.Query = "foo";
        Check.True(model.Find.FindNext(), "a first match");
        Check.Equal(new SelectionState(0, 3), model.Selection, "the first match from the caret");
        model.Find.FindNext();
        Check.Equal(new SelectionState(8, 11), model.Selection, "the next match");
        model.OnKey(EditorKey.F3, None);
        Check.Equal(new SelectionState(12, 15), model.Selection, "F3 goes to the match on the next line");
        model.Find.FindNext();
        Check.Equal(new SelectionState(0, 3), model.Selection, "past the last match it comes round");
        Check.True(model.Find.LastWrapped, "and says it wrapped");
        model.OnKey(EditorKey.F3, Shift);
        Check.Equal(new SelectionState(12, 15), model.Selection, "Shift+F3 from the first match wraps to the last");
        model.Find.FindPrevious();
        Check.Equal(new SelectionState(8, 11), model.Selection, "previous");
        Check.Equal(new FindCount(3, false), model.Find.CountMatches(), "the match count");

        var (words, _, _) = Editors.Make("foobar foo");
        words.Find.Query = "foo";
        words.Find.WholeWord = true;
        words.Find.FindNext();
        Check.Equal(new SelectionState(7, 10), words.Selection, "whole word skips foobar");

        var (cased, _, _) = Editors.Make("Foo foo");
        cased.Find.Query = "foo";
        cased.Find.MatchCase = true;
        cased.Find.FindNext();
        Check.Equal(new SelectionState(4, 7), cased.Selection, "match case skips Foo");

        var (capped, _, _) = Editors.Make("a a a a a", new CraftOptions { FindCountLimit = 3 });
        capped.Find.Query = "a";
        Check.Equal(new FindCount(3, true), capped.Find.CountMatches(), "counting stops at the limit");
    }

    [Test]
    public static void ReplaceAllIsOneUndoStep()
    {
        var (model, document, _) = Editors.Make("a x a\na");
        model.Find.Query = "a";
        Check.Equal(3, model.Find.ReplaceAll("bb"), "three replaced");
        Check.Equal("bb x bb\nbb", document.Text, "replace all");
        Check.Equal(1, document.Kinds.Count, "one edit");
        Check.Equal(EditKind.Command, document.Kinds[0], "replace all is a command");
        model.OnKey(EditorKey.Z, Ctrl);
        Check.Equal("a x a\na", document.Text, "one undo brings every match back");

        var (one, oneDocument, _) = Editors.Make("a b a");
        one.Find.Query = "a";
        one.Find.FindNext();
        Check.True(one.Find.ReplaceOne("c"), "the selected match is replaced");
        Check.Equal("c b a", oneDocument.Text, "replace one");
        Check.Equal(new SelectionState(4, 5), one.Selection, "and the next match is selected");
    }

    [Test]
    public static void BracketMatchSkipsStringsWhenClassified()
    {
        var (model, _, _) = Editors.MakeMarked("f(a[b]c)|");
        Check.Equal(new BracketMatch(1, 7), model.MatchingBrackets, "the bracket before the caret and its partner");
        model.Select(1, 1);
        Check.Equal(new BracketMatch(1, 7), model.MatchingBrackets, "the bracket at the caret");
        model.Select(4, 4);
        Check.Equal(new BracketMatch(3, 5), model.MatchingBrackets, "[ ] nested");
        var (adjacent, _, _) = Editors.MakeMarked("([|])");
        Check.Equal(new BracketMatch(1, 2), adjacent.MatchingBrackets, "the bracket before the caret wins over the one at it");

        const string text = "(\")\" x)";
        var (plain, _, _) = Editors.Make(text);
        plain.Select(0, 0);
        Check.Equal(new BracketMatch(0, 2), plain.MatchingBrackets, "plain text matches the ) inside the quotes");
        var language = new QuoteLanguage();
        var (classified, _, _) = Editors.Make(text, language: language);
        classified.Select(0, 0);
        Check.Equal(new BracketMatch(0, 6), classified.MatchingBrackets, "a classified string's ) is skipped");

        var (multi, _, _) = Editors.Make("{\n  \"}\"\n}", language: new QuoteLanguage());
        multi.Select(0, 0);
        Check.Equal(new BracketMatch(0, 8), multi.MatchingBrackets, "across lines, skipping a string's }");

        var (limited, _, _) = Editors.Make("{\n\n\n}", new CraftOptions { BracketScanLines = 1 });
        limited.Select(0, 0);
        Check.True(limited.MatchingBrackets is null, "the scan stops at the line limit");
    }

    [Test]
    public static void HitTestRoundTrips()
    {
        var (model, _, _) = Editors.Make("a\tbc\nxyz\n\n\U0001F600q");
        var snapshot = model.Snapshot;
        for (int p = 0; p <= snapshot.Length; p++)
        {
            if (p > 0 && p < snapshot.Length && char.IsLowSurrogate(snapshot[p]))
            {
                continue;
            }

            var line = snapshot.GetLine(snapshot.GetLineIndex(p));
            if (p > line.End)
            {
                continue;
            }

            var point = model.PointFromPosition(p);
            Check.Equal(p, model.PositionFromPoint(point.X + 1, point.Y + 1), $"position {p} -> ({point.X}, {point.Y}) -> position");
        }

        Check.Equal(model.TextLeft + 40, model.PointFromPosition(2).X, "after the tab is column 4");
        Check.Equal(0, model.PositionFromPoint(-5, -5), "above and left clamps to 0");
        Check.Equal(snapshot.Length, model.PositionFromPoint(10_000, 10_000), "below and right clamps to the end");
        Check.Equal(1, model.PositionFromPoint(model.TextLeft + 20, 1), "inside the tab's first half: before it");
        Check.Equal(2, model.PositionFromPoint(model.TextLeft + 30, 1), "inside its second half: after it");
    }

    [Test]
    public static void ClicksSelectWordsAndLines()
    {
        var time = new ManualTime();
        var (model, _, _) = Editors.Make("foo bar\nbaz", time: time);
        float x = model.TextLeft + 51;
        model.PointerPressed(x, 1, None);
        model.PointerReleased();
        Check.Equal(SelectionState.At(5), model.Selection, "a click puts the caret");
        time.AdvanceMilliseconds(100);
        model.PointerPressed(x, 1, None);
        model.PointerReleased();
        Check.Equal(new SelectionState(4, 7), model.Selection, "a double click selects the word");
        time.AdvanceMilliseconds(100);
        model.PointerPressed(x + 2, 2, None);
        model.PointerReleased();
        Check.Equal(new SelectionState(0, 8), model.Selection, "a triple click selects the line with its break");
        time.AdvanceMilliseconds(1000);
        model.PointerPressed(x, 1, None);
        Check.Equal(1, model.ClickCount, "a click after the double-click time counts 1");
        model.PointerMoved(model.TextLeft + 1, 21);
        Check.Equal(new SelectionState(5, 8), model.Selection, "dragging extends the selection");
        model.PointerReleased();
        time.AdvanceMilliseconds(1000);
        model.PointerPressed(model.TextLeft + 71, 1, Shift);
        Check.Equal(new SelectionState(5, 7), model.Selection, "shift-click extends from the anchor");
        model.PointerReleased();
        time.AdvanceMilliseconds(1000);
        model.PointerPressed(model.GutterWidth / 2, 21, None);
        Check.Equal(SelectionState.At(8), model.Selection, "a click in the gutter goes to the line's start");
    }

    [Test]
    public static void EscapeInsertAndAutoClose()
    {
        var (model, _, _) = Editors.MakeMarked("[ab] ab");
        model.Find.Query = "ab";
        Check.True(model.OnKey(EditorKey.Escape, None), "Escape handled");
        Check.True(!model.Find.IsHighlighting, "the first Escape clears the find highlight");
        Check.Equal(new SelectionState(0, 2), model.Selection, "and keeps the selection");
        model.OnKey(EditorKey.Escape, None);
        Check.Equal(SelectionState.At(2), model.Selection, "the second clears the selection");
        Check.True(!model.OnKey(EditorKey.Escape, None), "a third has nothing to do");

        var (overwrite, overwriteDocument, _) = Editors.MakeMarked("|ab");
        overwrite.OnKey(EditorKey.Insert, None);
        overwrite.OnText("X");
        Check.Equal("X|b", Editors.Marked(overwrite, overwriteDocument), "overwrite replaces the next character");

        var (off, offDocument, _) = Editors.MakeMarked("|");
        off.OnText("(");
        Check.Equal("(|", Editors.Marked(off, offDocument), "auto-close is off by default");

        var (on, onDocument, _) = Editors.MakeMarked("|", new CraftOptions { AutoCloseBrackets = true });
        on.OnText("(");
        Check.Equal("(|)", Editors.Marked(on, onDocument), "auto-close inserts the closing bracket");
        on.OnText(")");
        Check.Equal("()|", Editors.Marked(on, onDocument), "typing the closing bracket over the auto-inserted one moves over it");
        on.OnText(")");
        Check.Equal("())|", Editors.Marked(on, onDocument), "a bracket not auto-inserted is typed");
    }

    [Test]
    public static void ScrollingFollowsTheCaretAndPages()
    {
        var text = string.Join("\n", Enumerable.Range(1, 100).Select(i => "line " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var (model, _, _) = Editors.Make(text);
        var requests = new List<EditorScrollEventArgs>();
        model.ScrollRequested += (_, e) => requests.Add(e);
        model.OnKey(EditorKey.End, Ctrl);
        Check.True(requests.Count > 0, "Ctrl+End asks to scroll");
        Check.Equal(99f * 20 + 20 + 40 - 400, requests[^1].Y, "the last line shows with the two-line margin");
        model.OnKey(EditorKey.Home, Ctrl);
        Check.Equal(0f, requests[^1].Y, "Ctrl+Home scrolls back to the top");
        model.OnKey(EditorKey.PageDown, None);
        Check.Equal(20, model.CaretLine, "PageDown moves by the viewport's 20 lines");
        Check.Equal(400f - 40, model.ScrollY, "and scrolls by them, keeping the two-line margin above the caret");
        Check.Equal((100 + 3) * 20f, model.ContentHeight, "content height is the lines plus the bottom margin");
    }

    [Test]
    public static void OnlyVisibleLinesAreDrawnOnALargeDocument()
    {
        var text = new StringBuilder();
        for (int i = 0; i < 100_000; i++)
        {
            text.Append("line ").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" \"s\"\n");
        }

        var language = new QuoteLanguage();
        var (model, _, _) = Editors.Make(text.ToString(), language: language);
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        Check.Equal(21, renderer.LinesDrawn, "20 lines fill 400 units, plus one");
        var numbers = canvas.Texts.Where(t => t.X < model.GutterWidth).ToList();
        Check.Equal(21, numbers.Count, "a line number per drawn line");
        Check.Equal(1, language.ClassifyCalls, "one classification per frame");
        Check.True(language.CharactersClassified < 21 * 20, $"only the visible lines are classified ({language.CharactersClassified} characters)");

        canvas.Clear();
        float scrollY = 50_000 * 20;
        renderer.Draw(canvas, model, 0, scrollY, 800, 400, caretVisible: true);
        Check.Equal(new LineRange(50_000, 50_020), renderer.LastVisible, "the lines at the scroll offset");
        Check.True(canvas.Texts.Any(t => t.Text == "50001" && t.Top == 0), "line 50001's number at the top");
        Check.True(canvas.Texts.Any(t => t.Text == "line 50000 "), "line 50000's text");
        Check.True(model.GutterWidth == (6 + 2) * 10, "the gutter holds six digits plus the padding");
    }

    [Test]
    public static void SpansAreClippedAndPlainTextFillsTheRest()
    {
        var theme = EditorThemes.Get(EditorThemes.Dark);
        var (model, _, _) = Editors.Make("x \"s\" y", language: new QuoteLanguage());
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        var runs = canvas.Texts.Where(t => t.X >= model.TextLeft).ToList();
        Check.SequenceEqual(["x ", "\"s\"", " y"], runs.Select(r => r.Text).ToList(), "runs split where the style changes");
        Check.SequenceEqual([theme.Colors.Foreground, theme.StyleOf(TokenClasses.String).Argb, theme.Colors.Foreground], runs.Select(r => r.Argb).ToList(), "plain text where no span");
        Check.SequenceEqual([model.TextLeft, model.TextLeft + 20, model.TextLeft + 50], runs.Select(r => r.X).ToList(), "each run at its column");

        var (block, _, _) = Editors.Make("one\ntwo\nthree\nfour", language: new WholeTextLanguage());
        canvas.Clear();
        using var blockRenderer = new EditorRenderer();
        blockRenderer.Draw(canvas, block, 0, 40, 800, 40, caretVisible: false);
        var blockRuns = canvas.Texts.Where(t => t.X >= block.TextLeft).ToList();
        Check.SequenceEqual(["three", "four"], blockRuns.Select(r => r.Text).ToList(), "a span from before the range is clipped to each line");
        Check.True(blockRuns.All(r => r.Argb == theme.StyleOf(TokenClasses.Comment).Argb), "in the span's style");

        var (tabs, _, _) = Editors.Make("\tx");
        canvas.Clear();
        using var tabRenderer = new EditorRenderer();
        tabRenderer.Draw(canvas, tabs, 0, 0, 800, 400, caretVisible: false);
        var tabRun = canvas.Texts.Single(t => t.X >= tabs.TextLeft);
        Check.Equal(("    x", tabs.TextLeft), (tabRun.Text, tabRun.X), "tabs are expanded in a run");
    }

    [Test]
    public static void RunStringsAreCachedAcrossBlinkAndScrollInPlace()
    {
        var (model, _, _) = Editors.Make("alpha \"b\"\ngamma\ndelta", language: new QuoteLanguage());
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        long built = renderer.RunStringsBuilt;
        Check.True(built > 0, "the first frame builds the runs");
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        Check.Equal(built, renderer.RunStringsBuilt, "the caret's blink builds no run strings");
        model.OnKey(EditorKey.Right, None);
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        Check.Equal(built, renderer.RunStringsBuilt, "a caret move builds none");
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        Check.Equal(built, renderer.RunStringsBuilt, "a scroll in place builds none");
        Check.Equal(4L, built, "four runs: 'alpha ', '\"b\"', 'gamma', 'delta'");
        model.OnText("z");
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        Check.Equal(built + 2, renderer.RunStringsBuilt, "an edit rebuilds only its own line's two runs");
        model.Select(0, 0);
        model.OnKey(EditorKey.Enter, None);
        canvas.Clear();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        var drawn = canvas.Texts.Where(t => t.X >= model.TextLeft).Select(t => (t.Text, t.Top)).ToList();
        Check.SequenceEqual([("azlpha ", 20f), ("\"b\"", 20f), ("gamma", 40f), ("delta", 60f)], drawn, "lines moved down by Enter are drawn with their own text");
    }

    [Test]
    public static void DrawOrderSelectionCaretAndTelemetry()
    {
        var time = new ManualTime();
        var (model, _, _) = Editors.Make("abc\ndef", time: time);
        var theme = model.Theme;
        model.OnKey(EditorKey.Right, Shift);
        time.AdvanceMilliseconds(7);
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        Check.Equal(7.0, model.LastInputToPaintMilliseconds, "input to paint");
        Check.Equal(7.0, model.MaxInputToPaintMilliseconds, "the maximum");
        var labels = new List<string>();
        foreach (var c in canvas.Calls)
        {
            string label = c.Kind == "text" ? (c.X >= model.TextLeft ? "text" : "number")
                : c.X == 0 && c.Y == 0 && c.W == 800 ? "background"
                : c.X == 0 && c.Y == 0 && c.W == model.GutterWidth ? "gutter"
                : c.Argb == theme.Colors.CurrentLine ? "current"
                : c.Argb == theme.Colors.Selection ? "selection"
                : c.Argb == theme.Colors.Caret ? "caret" : c.Kind;
            if (labels.Count == 0 || labels[^1] != label)
            {
                labels.Add(label);
            }
        }

        Check.SequenceEqual(["background", "current", "selection", "text", "gutter", "number", "caret"], labels,
            "drawn in order: background, current line, selection, text, gutter, line numbers, caret");
        var selection = canvas.Calls.Single(c => c.Argb == theme.Colors.Selection);
        Check.Equal((model.TextLeft, 10f), (selection.X, selection.W), "the selection covers one cell");
        var caret = canvas.Calls.Single(c => c.Argb == theme.Colors.Caret);
        Check.Equal((model.TextLeft + 10, model.Options.CaretWidth), (caret.X, caret.W), "a bar caret after the selection");

        model.OnKey(EditorKey.Insert, None);
        canvas.Clear();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: true);
        Check.True(canvas.Calls.Any(c => c.Kind == "fill" && c.W == 10 && (c.Argb & 0x00FFFFFFu) == (theme.Colors.Caret & 0x00FFFFFFu)), "a block caret in overwrite mode");
        canvas.Clear();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        Check.True(!canvas.Calls.Any(c => (c.Argb & 0x00FFFFFFu) == (theme.Colors.Caret & 0x00FFFFFFu) && c.Kind == "fill"), "no caret while the blink hides it");
    }

    [Test]
    public static void ContentWidthGrowsWithLinesSeen()
    {
        var (model, _, _) = Editors.Make("ab\n" + new string('x', 300));
        using var renderer = new EditorRenderer();
        float before = model.ContentWidth;
        Check.Equal(model.TextLeft + (0 + 4 + 1) * 10, before, "nothing seen yet: the margin only");
        renderer.Draw(new RecordingCanvas(), model, 0, 0, 800, 0, caretVisible: false);
        Check.Equal(model.TextLeft + (2 + 4 + 1) * 10, model.ContentWidth, "the first line seen");
        renderer.Draw(new RecordingCanvas(), model, 0, 0, 800, 400, caretVisible: false);
        Check.Equal(model.TextLeft + (300 + 4 + 1) * 10, model.ContentWidth, "the long line seen");
    }

    [Test]
    public static void EventsAndLanguageName()
    {
        var (model, _, _) = Editors.MakeMarked("[word] x");
        int saves = 0;
        FindRequestedEventArgs? find = null;
        model.SaveRequested += (_, _) => saves++;
        model.FindRequested += (_, e) => find = e;
        Check.True(model.OnKey(EditorKey.S, Ctrl), "Ctrl+S handled");
        Check.Equal(1, saves, "Ctrl+S raises SaveRequested");
        model.OnKey(EditorKey.H, Ctrl);
        Check.True(find is { Replace: true, InitialQuery: "word" }, "Ctrl+H asks for replace with the selected word");
        Check.True(!model.OnKey(EditorKey.S, None), "S alone is text, not a command");
        Check.Equal("Plain text", model.LanguageName, "an untitled document of no language");

        var (xyz, _, _) = Editors.Make("", path: "notes.xyz");
        Check.Equal("Plain text — no language for .xyz", xyz.LanguageName, "a file of no language says why");
        var (named, _, _) = Editors.Make("", language: new QuoteLanguage());
        Check.Equal("quotes", named.LanguageName, "a language's name");
    }

    [Test]
    public static async Task ALandedParseRebuildsTheLinesWhoseSpansChanged()
    {
        var language = new DeferredLanguage();
        var (model, _, _) = Editors.Make("int a = 1;\nplain words\nb = 22;", language: language);
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        Check.True(!renderer.LastFrameClassified && renderer.ParseWaitsExceeded == 1, "before the parse lands, the frame's wait runs out and says so");
        uint number = model.Theme.StyleOf(TokenClasses.Number).Argb;
        Check.True(!canvas.Texts.Any(t => t.Argb == number), "and nothing is drawn as a number");
        long built = renderer.RunStringsBuilt;

        int raised = 0;
        int raisedOn = -1;
        model.Reclassified += (_, _) =>
        {
            raised++;
            raisedOn = Environment.CurrentManagedThreadId;
        };
        int ui = Environment.CurrentManagedThreadId;
        await Task.Factory.StartNew(() => language.Last!.Land(), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Check.Equal(1, raised, "the session's Reclassified reaches the model's");
        Check.True(raisedOn != ui, "it is raised on the parse's thread (the host marshals it)");

        int invalidated = 0;
        model.Invalidated += (_, _) => invalidated++;
        model.ApplyReclassification();
        Check.Equal(1, invalidated, "applied on the UI thread, it invalidates the view");
        canvas.Clear();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        Check.True(renderer.LastFrameClassified && renderer.ParseWaitsExceeded == 1, "after the landing the frame's spans are final");
        Check.SequenceEqual(["1", "22"], canvas.Texts.Where(t => t.Argb == number).Select(t => t.Text).ToList(), "the numbers are drawn in their colour now");
        // Line 0 "int a = " "1" ";" and line 2 "b = " "22" ";" are rebuilt (three runs each); "plain words" is not.
        Check.Equal(built + 6, renderer.RunStringsBuilt, "only the two lines whose spans changed are rebuilt");
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        Check.Equal(built + 6, renderer.RunStringsBuilt, "the next frame rebuilds nothing");
    }

    /// <summary>A language that classifies the whole text as one comment, whatever range it is asked for.</summary>
    private sealed class WholeTextLanguage : ILanguage
    {
        public string Name => "whole";

        public string Summary => "Test: one span over the whole text";

        public IReadOnlyList<string> Extensions { get; } = [".whole"];

        public LanguageRules Rules => LanguageRules.Plain;

        public ILanguageSession Open(ITextBuffer buffer) => new Session();

        private sealed class Session : ILanguageSession
        {
            public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into) => into.Add(0, snapshot.Length, TokenClasses.Comment);

            public void Dispose()
            {
            }
        }
    }
}
