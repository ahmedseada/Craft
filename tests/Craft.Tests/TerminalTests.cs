// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Settings;
using Craft.Editor;
using Craft.Editor.Terminal;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// The integrated terminal (phase T), headless: the VT parser and screen against fixed byte streams (colours, cursor,
/// erase, insert and delete, wide characters, the alternate screen, scroll regions, wrap, titles, modes, replies), the
/// scrollback cap with flat memory, the renderer drawing only the visible rows, the keys' VT bytes, the start folder in
/// its cases, the settings, and a session through the library's "pipe" host that runs input and reports its exit code.
/// </summary>
[TestGroup("Terminal")]
public static class TerminalTests
{
    private static TerminalScreen Fed(int columns, int rows, string text, int scrollback = 100)
    {
        var screen = new TerminalScreen(columns, rows, scrollback);
        screen.Feed(Encoding.UTF8.GetBytes(text));
        return screen;
    }

    [Test]
    public static void ColoursAndRenditionsFromSgr()
    {
        var screen = Fed(40, 4, "\e[31mr\e[0m \e[1;3;4;38;5;208mx\e[0;48;2;1;2;3my\e[38:2::10:20:30mz\e[38:5:99mw\e[7;9mi\e[22;27;29m\e[94;101mb\e[39;49mn");
        Check.Equal(TerminalColor.Indexed(1), screen.CellAt(0, 0).Foreground, "SGR 31 is indexed red");
        Check.Equal(TerminalColor.Default, screen.CellAt(0, 1).Foreground, "SGR 0 resets the colour");
        var x = screen.CellAt(0, 2);
        Check.True(x.Foreground == TerminalColor.Indexed(208) && x.Attributes == (TerminalAttributes.Bold | TerminalAttributes.Italic | TerminalAttributes.Underline),
            "38;5;208 with bold, italic and underline");
        Check.Equal(TerminalColor.Rgb(1, 2, 3), screen.CellAt(0, 3).Background, "48;2;r;g;b is a true-colour background");
        Check.Equal(TerminalColor.Rgb(10, 20, 30), screen.CellAt(0, 4).Foreground, "38:2::r:g:b (the colon form with a colour space)");
        Check.Equal(TerminalColor.Indexed(99), screen.CellAt(0, 5).Foreground, "38:5:n");
        Check.Equal(TerminalAttributes.Inverse | TerminalAttributes.Strike, screen.CellAt(0, 6).Attributes, "SGR 7 and 9");
        var bright = screen.CellAt(0, 7);
        Check.True(bright.Foreground == TerminalColor.Indexed(12) && bright.Background == TerminalColor.Indexed(9) && bright.Attributes == TerminalAttributes.None,
            "94 and 101 are the bright colours; 22, 27, 29 take the renditions off");
        Check.True(screen.CellAt(0, 8) is { Foreground: TerminalColor.Default, Background: TerminalColor.Default }, "39 and 49 are the defaults");
        Check.Equal("r xyzwibn", screen.RowText(0), "the text");
    }

    [Test]
    public static void CursorMovesAndSavedCursor()
    {
        var screen = Fed(20, 10, "\e[5;8HA\e[2AB\e[3BC\e[4DD\e[2CE\e[GF\e[3dG\e[10;20H\e[1;1fH");
        Check.Equal("H", screen.RowText(0), "HVP 1;1");
        Check.Equal("       A", screen.RowText(4), "CUP 5;8 writes at row 5, column 8");
        Check.Equal(" G      B", screen.RowText(2), "CUU 2 from row 5, then VPA 3 from column 2");
        Check.Equal("F     D  E", screen.RowText(5), "CUD 3, CUB 4, CUF 2 and CHA");
        var saved = Fed(20, 5, "\e[3;4H\e7\e[1;1Hx\e8y\e[2;2H\e[s\e[5;5H\e[uz");
        Check.Equal("   y", saved.RowText(2), "DECRC returns to where DECSC saved");
        Check.Equal(" z", saved.RowText(1), "SCORC returns to where SCOSC saved");
        var clamped = Fed(5, 3, "\e[99;99HX\e[99AY");
        Check.True(clamped.RowText(2) == "    X" && clamped.RowText(0) == "    Y", "moves stop at the screen's edges");
    }

    [Test]
    public static void EraseInsertAndDelete()
    {
        var screen = Fed(10, 4, "abcdefghij\r\nklmnopqrst\r\nuvwxyz\e[2;5H\e[K");
        Check.Equal("klmn", screen.RowText(1), "EL 0 erases to the end of the line");
        screen.Feed("\e[1;4H\e[1K"u8);
        Check.Equal("    efghij", screen.RowText(0), "EL 1 erases to the cursor, inclusive");
        screen.Feed("\e[3;3H\e[2X"u8);
        Check.Equal("uv  yz", screen.RowText(2), "ECH 2");
        screen.Feed("\e[3;2H\e[2@"u8);
        Check.Equal("u  v  yz", screen.RowText(2), "ICH 2 inserts blanks");
        screen.Feed("\e[3;1H\e[3P"u8);
        Check.Equal("v  yz", screen.RowText(2), "DCH 3 deletes");
        screen.Feed("\e[2;1H\e[L"u8);
        Check.True(screen.RowText(1).Length == 0 && screen.RowText(2) == "klmn" && screen.RowText(3) == "v  yz", "IL 1 pushes the lines below down");
        screen.Feed("\e[1;1H\e[2M"u8);
        Check.True(screen.RowText(0) == "klmn" && screen.RowText(1) == "v  yz" && screen.RowText(3).Length == 0, "DL 2 pulls the lines below up");
        screen.Feed("\e[1;3H\e[J"u8);
        Check.True(screen.RowText(0) == "kl" && screen.RowText(1).Length == 0, "ED 0 erases from the cursor to the end");
        var all = Fed(10, 3, "one\r\ntwo\r\nthree\e[2;2H\e[1J");
        Check.True(all.RowText(0).Length == 0 && all.RowText(1) == "  o" && all.RowText(2) == "three", "ED 1 erases from the start to the cursor");
        all.Feed("\e[2J"u8);
        Check.True(all.RowText(0).Length == 0 && all.RowText(2).Length == 0, "ED 2 erases the screen");
        var colored = Fed(6, 1, "\e[44m\e[2K");
        Check.Equal(TerminalColor.Indexed(4), colored.CellAt(0, 3).Background, "an erase leaves the current background (as xterm does)");
    }

    [Test]
    public static void WideCharactersTakeTwoCells()
    {
        var screen = Fed(10, 3, "中文a😀b");
        Check.True(screen.CellAt(0, 0) is { IsWide: true, CodePoint: 0x4E2D } && screen.CellAt(0, 1).IsWideTail, "a CJK character takes two cells");
        Check.True(screen.CellAt(0, 2).CodePoint == 0x6587 && screen.CellAt(0, 4).CodePoint == 'a', "the next character follows its second cell");
        Check.True(screen.CellAt(0, 5) is { IsWide: true, CodePoint: 0x1F600 } && screen.CellAt(0, 7).CodePoint == 'b', "an emoji takes two cells");
        Check.Equal("中文a😀b", screen.RowText(0), "the row's text");
        Check.Equal(8, screen.CursorColumn, "the cursor after 2+2+1+2+1 cells");
        var edge = Fed(5, 2, "abcd中");
        Check.True(edge.RowText(0) == "abcd" && edge.CellAt(1, 0).CodePoint == 0x4E2D && edge.ScreenRow(0).Wrapped, "a wide character at the last column wraps whole");
        var over = Fed(6, 1, "中文\e[1;2Hx");
        Check.True(over.CellAt(0, 0).IsBlank && over.CellAt(0, 1).CodePoint == 'x' && over.CellAt(0, 2).CodePoint == 0x6587, "writing over half of a wide character blanks its other half");
        var split = new TerminalScreen(10, 1, 0);
        var bytes = Encoding.UTF8.GetBytes("é😀");
        foreach (var b in bytes)
        {
            split.Feed([b]);
        }

        Check.Equal("é😀", split.RowText(0), "UTF-8 split across reads, byte by byte");
        var invalid = new TerminalScreen(10, 1, 0);
        invalid.Feed([(byte)'a', 0xFF, (byte)'b', 0xE4, 0xB8, (byte)'c']);
        Check.Equal("a\uFFFDb\uFFFDc", invalid.RowText(0), "a bad byte and a cut sequence each draw U+FFFD");
        var combining = Fed(10, 1, "e\u0301x");
        Check.Equal("ex", combining.RowText(0), "a combining mark takes no cell of its own");
    }

    [Test]
    public static void AlternateScreenKeepsTheMainOne()
    {
        var screen = Fed(10, 3, "main\r\nline\e[2;3H");
        screen.Feed("\e[?1049h"u8);
        Check.True(screen.IsAlternate && screen.RowText(0).Length == 0 && screen.ScrollbackLines == 0, "1049 shows a blank alternate screen without scrollback");
        screen.Feed("\e[Hfull\r\nscreen\r\n\r\n\r\nmore"u8);
        Check.Equal("more", screen.RowText(2), "the alternate screen scrolls in itself");
        screen.Feed("\e[?1049l"u8);
        Check.True(!screen.IsAlternate && screen.RowText(0) == "main" && screen.RowText(1) == "line", "leaving it shows the main screen as it was");
        Check.True(screen.CursorRow == 1 && screen.CursorColumn == 2, "and the cursor where it was");
        screen.Feed("\e[?47hx\e[?47l"u8);
        Check.Equal("line", screen.RowText(1), "47 switches too");
    }

    [Test]
    public static void ScrollRegionsScrollOnlyTheirRows()
    {
        var screen = Fed(10, 5, "top\r\n1\r\n2\r\n3\r\nbottom\e[2;4r\e[4;1H\n\nnew");
        Check.True(screen.RowText(0) == "top" && screen.RowText(4) == "bottom", "the rows outside the region stay");
        Check.True(screen.RowText(1) == "3" && screen.RowText(2).Length == 0 && screen.RowText(3) == "new", "the region scrolled up twice");
        Check.Equal(0, screen.ScrollbackLines, "a region's scroll keeps nothing in the scrollback");
        screen.Feed("\e[2;1H\eMup"u8);
        Check.True(screen.RowText(1) == "up" && screen.RowText(2) == "3", "RI at the region's top scrolls it down");
        screen.Feed("\e[r\e[5;1H\nlast"u8);
        Check.True(screen.ScrollbackLines == 1 && screen.RowText(4) == "last" && screen.LineAt(screen.FirstLine)?.Text() == "top", "DECSTBM reset: a full-screen scroll keeps the top line");
        var lines = Fed(10, 5, "a\r\nb\r\nc\r\nd\r\ne\e[2S");
        Check.True(lines.RowText(0) == "c" && lines.RowText(2) == "e" && lines.RowText(4).Length == 0, "SU 2");
        lines.Feed("\e[1T"u8);
        Check.True(lines.RowText(0).Length == 0 && lines.RowText(1) == "c", "SD 1");
    }

    [Test]
    public static void WrapTabsAndSelectionText()
    {
        var screen = Fed(5, 3, "abcdefgh\r\nx\ty\e[?7lzzzzzzzz");
        Check.True(screen.RowText(0) == "abcde" && screen.RowText(1) == "fgh" && screen.ScreenRow(0).Wrapped, "text wraps at the margin");
        Check.Equal("x   z", screen.RowText(2), "a tab stops at column 8 or the margin; with DECAWM off the last column is written over");
        Check.Equal("abcdefgh\nx   z", screen.TextOf(screen.ScreenTop, 0, screen.ScreenTop + 2, 5), "a wrapped line's text joins without a break");
        var tabs = Fed(30, 1, "a\tb\e[3G\eH\e[1Gc\td\e[Z\e[0g\e[1G\te");
        Check.Equal("c d     e", tabs.RowText(0), "HTS sets a stop, CBT goes back, TBC clears it");
    }

    [Test]
    public static void TitlesFoldersModesAndReplies()
    {
        var screen = Fed(20, 4, "\e]0;first\a\e]2;second\e\\\e]7;file://host/home/user/Craft%20Pages\a\e]9;9;\"C:\\Work\"\e\\");
        Check.Equal("second", screen.Title, "OSC 0 and 2 (BEL and ST) set the title");
        Check.Equal("C:\\Work", screen.WorkingDirectory, "OSC 9;9 sets the folder");
        Check.Equal("/home/user/Craft Pages", TerminalScreen_FolderOf("file://host/home/user/Craft%20Pages"), "OSC 7's file URI");
        Check.Equal("C:/x", TerminalScreen_FolderOf("file:///C:/x"), "a Windows path in OSC 7");
        screen.Feed("\e[?2004h\e[?25l\e[?1h\e[5 q\e[?1000h"u8);
        Check.True(screen.BracketedPaste && !screen.CursorVisible && screen.ApplicationCursorKeys && screen.CursorStyle == TerminalCursorStyle.Bar && screen.MouseTracking == 1000,
            "2004, 25, 1, DECSCUSR 5 and 1000");
        screen.Feed("\e[!p"u8);
        Check.True(!screen.BracketedPaste && screen.CursorVisible && !screen.ApplicationCursorKeys, "DECSTR resets the modes");
        var replies = new List<string>();
        screen.Respond = reply => replies.Add(Encoding.ASCII.GetString(reply));
        screen.Feed("\e[3;7H\e[6n\e[5n\e[c"u8);
        Check.SequenceEqual(["\e[3;7R", "\e[0n", "\e[?1;2c"], replies, "DSR 6, DSR 5 and DA answer the shell");
        var bells = Fed(5, 1, "\a\a\e]0;t\a");
        Check.Equal(2, bells.Bells, "BEL rings; an OSC's BEL ends it");
        var ignored = Fed(10, 1, "a\eP1$r0m\e\\b\e_apc\e\\c\e[?999hd");
        Check.Equal("abcd", ignored.RowText(0), "DCS, APC and unknown modes are skipped");
        var graphics = Fed(10, 1, "\e(0lqk\e(Bq");
        Check.Equal("┌─┐q", graphics.RowText(0), "ESC ( 0 draws DEC line drawing, ESC ( B ASCII again");
    }

    private static string? TerminalScreen_FolderOf(string uri)
    {
        var screen = new TerminalScreen(10, 1, 0);
        screen.Feed(Encoding.UTF8.GetBytes("\e]7;" + uri + "\a"));
        return screen.WorkingDirectory;
    }

    [Test]
    public static void ScrollbackIsCappedAndMemoryStaysFlat()
    {
        var screen = new TerminalScreen(80, 24, 1000);
        var line = Encoding.ASCII.GetBytes(new string('x', 70) + "\r\n");
        for (int i = 0; i < 5000; i++)
        {
            screen.Feed(line);
        }

        Check.Equal(1000, screen.ScrollbackLines, "the scrollback holds CRAFT_TERMINAL_SCROLLBACK lines");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++)
        {
            screen.Feed(line);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check.True(allocated < 64 * 1024, $"100,000 lines once the ring is full allocate nothing per line (the oldest row is reused): {allocated:N0} bytes");
        Check.Equal(1000, screen.ScrollbackLines, "still capped");
        var numbered = new TerminalScreen(20, 3, 5);
        numbered.Feed(Encoding.ASCII.GetBytes(string.Join("\r\n", Enumerable.Range(1, 20).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        Check.True(numbered.LineAt(numbered.FirstLine)?.Text() == "13" && numbered.RowText(2) == "20" && numbered.LineAt(numbered.FirstLine - 1) is null,
            "the oldest kept line is the cap's; older ones are gone");
        numbered.Feed("\e[3J"u8);
        Check.True(numbered.ScrollbackLines == 0 && numbered.RowText(2) == "20", "ED 3 drops the scrollback, the screen stays");
        numbered.Feed("\r\n21\r\n22"u8);
        numbered.Clear();
        Check.True(numbered.RowText(0) == "22" && numbered.RowText(1).Length == 0 && numbered.ScrollbackLines == 0 && numbered.CursorRow == 0, "Clear keeps the cursor's row at the top");
    }

    [Test]
    public static void ResizeKeepsTheCursorOnItsText()
    {
        var screen = Fed(10, 6, "one\r\ntwo\r\nthree");
        screen.Resize(10, 4);
        Check.True(screen.RowText(0) == "one" && screen.RowText(2) == "three" && screen.CursorRow == 2 && screen.ScrollbackLines == 0, "fewer rows drop the blank rows under the cursor first");
        screen.Resize(10, 2);
        Check.True(screen.RowText(1) == "three" && screen.CursorRow == 1 && screen.ScrollbackLines == 1, "then the top rows go into the scrollback");
        screen.Resize(10, 5);
        Check.True(screen.RowText(0) == "one" && screen.RowText(2) == "three" && screen.CursorRow == 2 && screen.ScrollbackLines == 0, "more rows bring the scrollback's lines back");
        screen.Resize(4, 5);
        Check.True(screen.RowText(2) == "thre" && screen.Columns == 4 && screen.CursorColumn <= 3, "narrower rows are cut");
        screen.Feed("\r\nabcdef"u8);
        Check.Equal("ef", screen.RowText(4), "the new width wraps");
    }

    [Test]
    public static void RendererDrawsOnlyTheVisibleRows()
    {
        var screen = new TerminalScreen(40, 24, 500);
        for (int i = 0; i < 200; i++)
        {
            screen.Feed(Encoding.ASCII.GetBytes($"line {i} \e[32mgreen\e[0m tail\r\n"));
        }

        var palette = new TerminalPalette(EditorThemes.Get(EditorThemes.Dark));
        var renderer = new TerminalRenderer();
        var canvas = new RecordingCanvas();
        var metrics = new FontMetrics(10, 20, 15);
        renderer.Draw(canvas, screen, palette, metrics, 400, 200, 0, null, focused: true, cursorOn: true);
        Check.Equal(10, renderer.RowsDrawn, "a 200-unit viewport of 20-unit rows draws 10 rows");
        Check.True(canvas.Texts.Contains("line 177") && canvas.Texts.Contains("green") && canvas.Texts.Contains("tail"), "the rows' runs: " + string.Join("|", canvas.Texts.Take(8)));
        Check.True(canvas.Styles.Contains(palette[2]), "the green run in the palette's green");
        long built = renderer.RunStringsBuilt;
        canvas.Clear();
        renderer.Draw(canvas, screen, palette, metrics, 400, 200, 0, null, focused: true, cursorOn: false);
        Check.Equal(built, renderer.RunStringsBuilt, "a still frame makes no string (the rows keep their runs)");
        renderer.Draw(canvas, screen, palette, metrics, 400, 200, 100, null, focused: true, cursorOn: true);
        Check.True(renderer.RunStringsBuilt > built && renderer.RowsDrawn == 10, "scrolled back 100 lines: those rows are drawn");
        canvas.Clear();
        var selection = new TerminalSelection(screen.ScreenTop, 0, screen.ScreenTop, 4);
        renderer.Draw(canvas, screen, palette, metrics, 400, 200, 0, selection, focused: false, cursorOn: true);
        Check.True(canvas.Fills.Contains((0f, 0f, 40f, palette.Selection)), "the selection's fill over its cells");
        var wide = Fed(10, 2, "中x");
        canvas.Clear();
        new TerminalRenderer().Draw(canvas, wide, palette, metrics, 100, 40, 0, null, true, false);
        Check.True(canvas.TextsAt.Contains(("中", 0f)) && canvas.TextsAt.Contains(("x", 20f)), "a wide character is its own run, the next one at its cell: " + string.Join("|", canvas.TextsAt));
        var themes = new TerminalPalette(EditorThemes.Get(EditorThemes.Light));
        for (int i = 1; i < 7; i++)
        {
            Check.True(ColorContrast.Ratio(themes[i], themes.Background) >= ColorContrast.Text && ColorContrast.Ratio(palette[i], palette.Background) >= ColorContrast.Text,
                $"basic colour {i} reads at 4.5:1 on both themes' ground");
        }
    }

    [Test]
    public static void KeysAsVtBytes()
    {
        static string Key(EditorKey key, KeyModifiers modifiers = KeyModifiers.None, bool application = false)
        {
            Span<byte> bytes = stackalloc byte[TerminalKeys.MaxLength];
            return Encoding.ASCII.GetString(bytes[..TerminalKeys.Encode(key, modifiers, application, bytes)]);
        }

        Check.Equal("\e[A", Key(EditorKey.Up), "Up");
        Check.Equal("\eOA", Key(EditorKey.Up, application: true), "Up in application mode");
        Check.Equal("\e[1;5C", Key(EditorKey.Right, KeyModifiers.Control), "Ctrl+Right");
        Check.Equal("\e[1;2H", Key(EditorKey.Home, KeyModifiers.Shift), "Shift+Home");
        Check.Equal("\e[5~", Key(EditorKey.PageUp), "Page Up");
        Check.Equal("\e[3;3~", Key(EditorKey.Delete, KeyModifiers.Alt), "Alt+Delete");
        Check.Equal("\eOP", Key(EditorKey.F1), "F1");
        Check.Equal("\e[15~", Key(EditorKey.F5), "F5");
        Check.Equal("\e[24;5~", Key(EditorKey.F12, KeyModifiers.Control), "Ctrl+F12");
        Check.Equal("\x03", Key(EditorKey.C, KeyModifiers.Control), "Ctrl+C");
        Check.Equal("\x1a", Key(EditorKey.Z, KeyModifiers.Control), "Ctrl+Z");
        Check.Equal("\ex", Key(EditorKey.X, KeyModifiers.Alt), "Alt+x is ESC x");
        Check.Equal("\eX", Key(EditorKey.X, KeyModifiers.Alt | KeyModifiers.Shift), "Alt+Shift+x");
        Check.Equal("", Key(EditorKey.Q, KeyModifiers.Control | KeyModifiers.Alt), "Ctrl+Alt+letter is AltGr: left to the text");
        Check.Equal("", Key(EditorKey.A), "a plain letter is text");
        Check.Equal("\r", Key(EditorKey.Enter), "Enter");
        Check.Equal("\e[Z", Key(EditorKey.Tab, KeyModifiers.Shift), "Shift+Tab");
        Check.Equal("\x7f", Key(EditorKey.Backspace), "Backspace");
        Check.Equal("\x08", Key(EditorKey.Backspace, KeyModifiers.Control), "Ctrl+Backspace");
        Check.Equal("\0", Key(EditorKey.Space, KeyModifiers.Control), "Ctrl+Space");
        Check.Equal("\e1", Key(EditorKey.D1, KeyModifiers.Alt), "Alt+1");
        Check.Equal("a\rb\r", Encoding.UTF8.GetString(TerminalKeys.EncodePaste("a\r\nb\n", bracketed: false)), "a paste's line breaks are Enter");
        Check.Equal("\e[200~xy\e[201~", Encoding.UTF8.GetString(TerminalKeys.EncodePaste("x\e[201~y", bracketed: true)), "a bracketed paste cannot end its own brackets");
        Check.True(KeyChord.Parse("Ctrl+`") == new KeyChord(EditorKey.Backquote, KeyModifiers.Control) && KeyChord.Parse("ctrl+shift+backquote").ToString() == "Ctrl+Shift+Backquote",
            "the backquote's chords");
    }

    [Test]
    public static void StartFolderFollowsTheOwnersRule()
    {
        using var folder = new TempFolder();
        var workspace = Directory.CreateDirectory(folder.File("workspace")).FullName;
        var chosen = Directory.CreateDirectory(folder.File("chosen")).FullName;
        var custom = Directory.CreateDirectory(folder.File("custom")).FullName;
        var home = Directory.CreateDirectory(folder.File("home")).FullName;
        var open = TerminalLaunch.StartFolder(null, null, workspace, home);
        Check.True(open.Folder == workspace && open.Reason == "the open folder", "a folder open: its root");
        var fileOnly = TerminalLaunch.StartFolder(null, null, null, home);
        Check.True(fileOnly.Folder == home && fileOnly.Reason == "the home folder", "only files open: the shell's default, the home folder");
        var variable = TerminalLaunch.StartFolder(null, custom, workspace, home);
        Check.True(variable.Folder == custom && variable.Reason == "CRAFT_TERMINAL_CWD", "CRAFT_TERMINAL_CWD overrides the open folder");
        Check.Equal(custom, TerminalLaunch.StartFolder(null, custom, null, home).Folder, "and the home folder");
        Check.Equal(chosen, TerminalLaunch.StartFolder(chosen, custom, workspace, home).Folder, "the explorer's \"Open in terminal\" starts in its folder");
        var missing = TerminalLaunch.StartFolder(null, folder.File("gone"), workspace, home);
        Check.True(missing.Folder == workspace && missing.Note is { } note && note.Contains("CRAFT_TERMINAL_CWD", StringComparison.Ordinal), "a folder that does not exist is passed over, said so");
    }

    [Test]
    public static void SettingsShellAndEnvironment()
    {
        var options = CraftOptions.FromVariables(name => name switch
        {
            "CRAFT_TERMINAL_ENV" => "A=1|PATH=/tools:${PATH}|GONE=",
            "CRAFT_TERMINAL_SHELL" => "bash",
            "CRAFT_TERMINAL_ARGS" => "-l",
            _ => null,
        });
        var environment = TerminalLaunch.Environment(new Hashtable { ["PATH"] = "/bin", ["GONE"] = "x", ["KEEP"] = "k" }, options.TerminalEnvironment);
        Check.True(environment["A"] == "1" && environment["PATH"] == "/tools:/bin" && !environment.ContainsKey("GONE") && environment["KEEP"] == "k" && environment["TERM_PROGRAM"] == "Craft",
            "CRAFT_TERMINAL_ENV adds, expands ${NAME} and removes over the app's environment");
        Check.Equal(new TerminalShell("bash", "-l"), TerminalLaunch.Shell(options, TerminalHosts.Get(TerminalHosts.Pipe)), "CRAFT_TERMINAL_SHELL and CRAFT_TERMINAL_ARGS");
        Check.Equal(TerminalHosts.Get(TerminalHosts.Pipe).DefaultShell(), TerminalLaunch.Shell(new CraftOptions(), TerminalHosts.Get(TerminalHosts.Pipe)), "no setting: the host's default shell");
        Check.Throws<ArgumentException>(() => CraftOptions.FromVariables(n => n == "CRAFT_TERMINAL_ENV" ? "novalue" : null), "an entry without '='");
        foreach (var variable in (ReadOnlySpan<string>)["CRAFT_TERMINAL_SHELL", "CRAFT_TERMINAL_ARGS", "CRAFT_TERMINAL_ENV", "CRAFT_TERMINAL_HOST"])
        {
            Check.True(!CraftSettings.Allows(SettingLayer.Workspace, variable) && CraftSettings.Allows(SettingLayer.User, variable), $"a workspace cannot set {variable} (what the terminal runs); the user can");
        }

        Check.True(CraftSettings.Allows(SettingLayer.Workspace, "CRAFT_TERMINAL_CWD"), "a workspace may set its start folder");
        Check.Equal(TerminalHosts.Pipe, TerminalHosts.Resolve("auto").Name, "auto with no platform host is pipe");
        Check.Throws<NotRegisteredException>(() => TerminalHosts.Resolve("no-such-host"), "an unknown host is not registered");
        Check.Equal(Path.Combine("/usr/bin", "pwsh"), TerminalLaunch.FindOnPath("pwsh", "/bin:/usr/bin", ':', null, p => p == Path.Combine("/usr/bin", "pwsh")), "a program found on the PATH");
        Check.Equal(Path.Combine("C:/ps", "pwsh.EXE"), TerminalLaunch.FindOnPath("pwsh", "C:/x;C:/ps", ';', ".COM;.EXE", p => p == Path.Combine("C:/ps", "pwsh.EXE")), "with PATHEXT");
        Check.Equal("pwsh — Craft", TerminalLaunch.TitleOf(new TerminalShell(@"C:\Program Files\PowerShell\7\pwsh.exe"), Path.Combine("projects", "Craft")), "a tab's title");
    }

    private static (TerminalShell Shell, string Script) EchoScript()
    {
        var host = TerminalHosts.Get(TerminalHosts.Pipe);
        var shell = host.DefaultShell();
        return OperatingSystem.IsWindows()
            // Not "set /p": cmd reading a pipe takes a whole buffer for it and drops every line after the first, so the
            // lines after it (echo, exit) never ran and cmd waited for more (the owner's Windows run).
            ? (shell, "set L=typed-text\recho echo:%L%\rexit 3\r")
            : (shell, "read line\rtyped-text\recho \"echo:$line\"\rexit 3\r");
    }

    [Test]
    public static async Task APipeSessionRunsInputAndReportsItsExitCode()
    {
        using var folder = new TempFolder();
        var host = TerminalHosts.Get(TerminalHosts.Pipe);
        var (shell, script) = EchoScript();
        var environment = TerminalLaunch.Environment(Environment.GetEnvironmentVariables(), []);
        using var terminal = TerminalInstance.Start(1, host, new TerminalStartInfo(shell, folder.Path, new TerminalSize(80, 24), environment), 1000, static action => action());
        terminal.Write(Encoding.UTF8.GetBytes(script));
        var done = await Task.WhenAny(terminal.Completion, Task.Delay(TimeSpan.FromSeconds(30)));
        Check.True(done == terminal.Completion, "the shell ended within 30 s");
        string text;
        lock (terminal.Screen.Sync)
        {
            text = terminal.Screen.TextOf(terminal.Screen.FirstLine, 0, terminal.Screen.ScreenTop + terminal.Screen.Rows - 1, terminal.Screen.Columns);
        }

        Check.True(text.Contains("echo:typed-text", StringComparison.Ordinal), "the input reached the shell and its answer the screen: " + text);
        Check.Equal(3, terminal.ExitCode ?? await terminal.Exited, "the exit code");
        Check.True(text.Contains("exit code 3", StringComparison.Ordinal), "the end is shown on the screen");
        Check.True(terminal.BytesRead > 0 && terminal.HostName == TerminalHosts.Pipe, "the bytes were read through the pipe host");
    }

    [Test]
    public static async Task TheManagerOpensByTheRuleAndPluginsWriteThroughTheHost()
    {
        using var folder = new TempFolder();
        var options = new CraftOptions { TerminalHost = TerminalHosts.Pipe };
        using var host = new EditorHost(options);
        host.WorkspaceFolder = folder.Path;
        var clipboard = new FakeClipboard();
        using var manager = new TerminalManager(host, () => options, clipboard) { HomeFolder = folder.Path };
        host.Terminals = manager;
        Check.True(EditorCommands.Find(TerminalCommandIds.New) is not null && EditorKeyBindings.Find(KeyChord.Parse("Ctrl+Shift+`"))?.CommandId == TerminalCommandIds.New
            && EditorKeyBindings.Find(KeyChord.Parse("Ctrl+`"))?.CommandId == TerminalCommandIds.Toggle, "the commands and their chords are registered");
        Check.True(manager.KeepsChord(KeyChord.Parse("Ctrl+Shift+C")) && !manager.KeepsChord(KeyChord.Parse("Ctrl+R")), "the terminal's chords run their commands; others go to the shell");
        var requests = new List<TerminalViewRequest>();
        manager.ViewRequested += (_, request) => requests.Add(request);
        Check.True(await host.ExecuteCommandAsync(TerminalCommandIds.New), "New terminal runs");
        var terminal = manager.ActiveTerminal!;
        Check.True(terminal.WorkingDirectory == Path.GetFullPath(folder.Path) && requests is [TerminalViewRequest.Show], "it starts in the open folder and is shown");
        IEditorHost plugin = host;
        var handle = plugin.Terminals!.Open(new EditorTerminalRequest { Name = "plug-in's", Show = false });
        Check.True(manager.Terminals.Count == 2 && handle.Title == "plug-in's" && ReferenceEquals(manager.ActiveTerminal, manager.Terminals[1]), "a plug-in opens a terminal through IEditorHost.Terminals");
        Check.True(handle.Write(OperatingSystem.IsWindows() ? "exit 4\r" : "exit 4\r"), "and writes to it");
        var ended = await Task.WhenAny(manager.Terminals[1].Completion, Task.Delay(TimeSpan.FromSeconds(30)));
        Check.True(ended == manager.Terminals[1].Completion && await handle.Exited == 4, "what it wrote ran");
        Check.True(!handle.Write("x"), "a terminal that ended takes no more");
        Check.True(await host.ExecuteCommandAsync(TerminalCommandIds.Previous) && ReferenceEquals(manager.ActiveTerminal, terminal), "previous terminal");
        var document = host.Open(TextDocument.Create(OperatingSystem.IsWindows() ? "echo run-sel\r\nexit 5" : "echo run-sel\nexit 5", options), null);
        host.SetActive(document);
        host.UpdateSelection(document, new SelectionState(0, document.Document.Buffer.Current.Length));
        Check.True(await host.ExecuteCommandAsync(TerminalCommandIds.RunSelection), "run the selection in the terminal");
        var ran = await Task.WhenAny(terminal.Completion, Task.Delay(TimeSpan.FromSeconds(30)));
        Check.True(ran == terminal.Completion && terminal.ExitCode == 5, "the selection's lines ran in the active terminal");
        string text;
        lock (terminal.Screen.Sync)
        {
            text = terminal.Screen.TextOf(terminal.Screen.FirstLine, 0, terminal.Screen.ScreenTop + terminal.Screen.Rows - 1, terminal.Screen.Columns);
        }

        Check.True(text.Contains("run-sel", StringComparison.Ordinal), "its output is on the screen: " + text);
        terminal.Selection = new TerminalSelection(terminal.Screen.FirstLine, 0, terminal.Screen.FirstLine, 7);
        Check.True(await host.ExecuteCommandAsync(TerminalCommandIds.Copy) && clipboard.Text is { Length: > 0 }, "copy puts the selection on the clipboard");
        Check.True(await host.ExecuteCommandAsync(TerminalCommandIds.Kill) && manager.Terminals.Count == 1, "kill closes the active terminal's tab");
        manager.Dispose();
        Check.True(EditorCommands.Find(TerminalCommandIds.New) is null && EditorKeyBindings.Find(KeyChord.Parse("Ctrl+`")) is null, "disposed, the commands and chords are taken back");
    }

    /// <summary>A canvas that keeps what was drawn.</summary>
    private sealed class RecordingCanvas : IEditorCanvas
    {
        public List<string> Texts { get; } = [];

        public List<(string, float)> TextsAt { get; } = [];

        public List<uint> Styles { get; } = [];

        public List<(float, float, float, uint)> Fills { get; } = [];

        public void Clear()
        {
            Texts.Clear();
            TextsAt.Clear();
            Styles.Clear();
            Fills.Clear();
        }

        public void FillRect(float x, float y, float width, float height, uint argb) => Fills.Add((x, y, width, argb));

        public void DrawLine(float x1, float y1, float x2, float y2, uint argb, float thickness)
        {
        }

        public void DrawText(string text, float x, float top, in TextStyle style)
        {
            Texts.Add(text);
            TextsAt.Add((text, x));
            Styles.Add(style.Argb);
        }
    }
}
