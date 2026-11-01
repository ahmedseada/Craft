// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// A semantic language the split editor's tests count: the sessions it opened and disposed, the contexts it was opened
/// with, a definition the test sets. Registered for its own extension while a test runs.
/// </summary>
internal sealed class CountingLanguage(string name, string extension) : ILanguage
{
    public string Name { get; } = name;

    public string Summary => "Test: counts its sessions";

    public IReadOnlyList<string> Extensions { get; } = [extension];

    public LanguageRules Rules => LanguageRules.Plain;

    public int Opened { get; private set; }

    public int Disposed { get; set; }

    public List<LanguageOpenContext> Contexts { get; } = [];

    public DefinitionLocation? Definition { get; set; }

    public ILanguageSession Open(ITextBuffer buffer) => Open(buffer, LanguageOpenContext.None);

    public ILanguageSession Open(ITextBuffer buffer, LanguageOpenContext context)
    {
        Opened++;
        Contexts.Add(context);
        return new Session(this);
    }

    internal sealed class Session(CountingLanguage language) : ISemanticLanguageSession
    {
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

        public Task<CompletionList?> GetCompletionsAsync(ITextSnapshot snapshot, int position, CompletionTrigger trigger, CancellationToken cancellationToken) => Task.FromResult<CompletionList?>(null);

        public Task<CompletionItem> ResolveCompletionAsync(CompletionList list, CompletionItem item, CancellationToken cancellationToken) => Task.FromResult(item);

        public Task<SignatureHelp?> GetSignatureHelpAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken) => Task.FromResult<SignatureHelp?>(null);

        public Task<HoverInfo?> GetHoverAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken) => Task.FromResult<HoverInfo?>(null);

        public Task<DefinitionLocation?> GetDefinitionAsync(ITextSnapshot snapshot, int position, CancellationToken cancellationToken) => Task.FromResult(language.Definition);

        public Task<IReadOnlyList<TextEdit>> FormatAsync(ITextSnapshot snapshot, int start, int length, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TextEdit>>([]);

        public void Release()
        {
        }

        public void Dispose() => language.Disposed++;
    }
}

/// <summary>
/// The shell headless for the split editor's tests: a workspace folder, the shell's editors made as the app's views make
/// them (one <see cref="EditorModel"/> per tab, its language session taken from the shell's pool), the prompts answered
/// and counted, a language registered for ".grp".
/// </summary>
internal sealed class SplitFixture : IDisposable
{
    private readonly Dictionary<DocumentTab, (EditorModel Model, LanguageSessionLease? Lease)> _editors = new(ReferenceEqualityComparer.Instance);

    public SplitFixture(TempFolder folder, Func<string, string?>? variables = null, Func<string, CraftOptions, CancellationToken, Task<ITextDocument>>? openDocument = null)
    {
        Folder = folder;
        var settings = new CraftSettings(variables ?? (_ => null), SettingsFile.Load(folder.File("data/settings.json")));
        Host = new EditorHost(new CraftOptions { ActivityMinimumMilliseconds = 0 });      // every operation's activity is kept, however quick
        Shell = new ShellModel(Host, settings, RecentList.Load(folder.File("data/recent.json"), 10), new NoDialogs(), Ui, openDocument);
        Shell.EditorOf = EditorOf;
        Language = new CountingLanguage("split-test", ".grp");
        EditorLanguages.Register(Language);
    }

    public TempFolder Folder { get; }

    public EditorHost Host { get; }

    public ShellModel Shell { get; }

    public CountingLanguage Language { get; }

    public CountingUi Ui { get; } = new();

    public Queue<Action> UiQueue { get; } = new();

    public string Workspace => Path.Combine(Folder.Path, "ws");

    /// <summary>The editor of a tab, made the first time as the app's view makes it (its session from the shell's pool).</summary>
    public EditorModel EditorOf(DocumentTab tab)
    {
        if (_editors.TryGetValue(tab, out var made))
        {
            return made.Model;
        }

        var options = Shell.Options with { NewLine = "\n" };
        var lease = Shell.Sessions.Acquire(tab.Document, tab.Language, EditorModel.OpenContext(tab.Document, Shell.Workspace?.Root, options, null));
        var model = new EditorModel(tab.Document, lease?.Session, tab.Language, options, EditorThemes.Get(EditorThemes.Dark), new FixedMeasurer(), new FakeClipboard());
        model.Dispatch = UiQueue.Enqueue;
        model.UpdateViewport(0, 0, 1200, 800);
        _editors[tab] = (model, lease);
        return model;
    }

    /// <summary>Lets a tab's editor go as the app's view does when its tab closes.</summary>
    public void Drop(DocumentTab tab)
    {
        if (_editors.Remove(tab, out var made))
        {
            made.Model.Dispose();
            made.Lease?.Dispose();
        }
    }

    public void Pump()
    {
        while (UiQueue.TryDequeue(out var action))
        {
            action();
        }
    }

    public string Write(string relative, string text)
    {
        var path = Path.Combine(Workspace, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public async Task<ShellModel> OpenWorkspaceAsync()
    {
        Directory.CreateDirectory(Workspace);
        Check.True(await Shell.OpenFolderAsync(Workspace), "the workspace opens");
        return Shell;
    }

    public void Dispose()
    {
        foreach (var (model, lease) in _editors.Values)
        {
            model.Dispose();
            lease?.Dispose();
        }

        _editors.Clear();
        Shell.Dispose();
        Host.Dispose();
        EditorLanguages.Unregister(Language.Name);
    }

    internal sealed class CountingUi : IShellUi
    {
        public int UnsavedQuestions { get; private set; }

        public UnsavedChoice Answer { get; set; } = UnsavedChoice.Discard;

        public List<string> Messages { get; } = [];

        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default)
        {
            UnsavedQuestions++;
            return Task.FromResult(Answer);
        }

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message)
        {
            Messages.Add(title + ": " + message);
            return Task.CompletedTask;
        }
    }

    private sealed class NoDialogs : IFileDialogs
    {
        public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickSavePathAsync(string suggestedName, string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
}

/// <summary>
/// The X restart's split editor (piece 3a), headless: groups with their own tabs, one focused (the host's active document
/// follows it), one document in two groups as one text, one host document and one language session (counted), split right
/// and down, move to the next group, drops, close a group, the keys in both keymaps, the layout kept per workspace and
/// read back, and the measure of a 100,000-line document in two groups.
/// </summary>
[TestGroup("EditorGroups")]
public static class EditorGroupsTests
{
    [Test]
    public static async Task SplitRightShowsTheDocumentInTwoGroupsWithOneTextOneHostDocumentAndOneSession()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var path = fixture.Write("a.grp", "one\ntwo\nthree\n");
        var first = (await shell.OpenFileAsync(path))!;
        var left = fixture.EditorOf(first);
        Check.Equal(1, fixture.Language.Opened, "the first view opens the document's session");

        Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.SplitRight), "Ctrl+\\ runs");
        var groups = shell.Groups;
        Check.Equal(2, groups.Groups.Count, "two groups");
        Check.True(ReferenceEquals(groups.Focused, groups.Groups[1]), "the new group, to the right, is focused");
        Check.True(groups.Root is { Group: null, Orientation: GroupOrientation.Row, Children.Count: 2 }, "a row of two");
        var second = shell.Tabs.Active!;
        Check.True(!ReferenceEquals(first, second) && ReferenceEquals(first.Document, second.Document) && ReferenceEquals(first.HostDocument, second.HostDocument),
            "the new group's tab shows the same text document and host document: one copy of the text");
        Check.Equal(1, fixture.Host.Documents.Count, "the host holds the document once");
        var right = fixture.EditorOf(second);
        Check.True(ReferenceEquals(left.LanguageSession, right.LanguageSession) && fixture.Language.Opened == 1 && shell.Sessions.Count == 1 && shell.Sessions.UsesOf(first.Document) == 2,
            "both views share one language session, counted twice");
        Check.True(!left.OwnsLanguageSession && !right.OwnsLanguageSession, "neither view owns the shared session");

        // Each view keeps its own caret; an edit in one shows in the other, whose caret follows it.
        left.Select(0, 0);
        right.Select(8, 8);
        left.OnText("zero\n");
        Check.Equal("zero\none\ntwo\nthree\n", second.Document.Buffer.Current.GetText(0, second.Document.Buffer.Current.Length), "one text: the right view sees the left's edit");
        Check.Equal(13, right.Selection.Caret, "the right view's caret moved with the edit before it");
        Check.Equal(5, left.Selection.Caret, "the left view's caret is its own");

        // Closing one of the two tabs of an edited document asks nothing and keeps it open; the last tab asks.
        Check.True(await shell.CloseTabAsync(second) && fixture.Ui.UnsavedQuestions == 0, "the second tab of an edited document closes without a question");
        fixture.Drop(second);
        Check.Equal(1, groups.Groups.Count, "the group left empty closed (CRAFT_EDITOR_GROUPS_CLOSE_EMPTY)");
        Check.True(fixture.Host.Documents.Count == 1 && first.Document.IsDirty && fixture.Language.Disposed == 0, "the document, its changes and its session stay with the first tab");
        Check.True(await shell.CloseTabAsync(first) && fixture.Ui.UnsavedQuestions == 1, "its last tab asks about the unsaved changes");
        fixture.Drop(first);
        Check.True(fixture.Host.Documents.Count == 0 && fixture.Language.Disposed == 1 && shell.Sessions.Count == 0, "the last view let the session go: disposed once");
    }

    [Test]
    public static async Task TheFocusedGroupIsTheHostsActiveDocumentAndCtrlNumbersFocusOrMakeGroups()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder, name => name == "CRAFT_EDITOR_GROUPS_MAX" ? "3" : null);
        var shell = await fixture.OpenWorkspaceAsync();
        var a = (await shell.OpenFileAsync(fixture.Write("a.txt", "a")))!;
        var b = (await shell.OpenFileAsync(fixture.Write("b.txt", "b")))!;
        var groups = shell.Groups;
        var right = groups.Split(groups.Focused, SplitDirection.Right, b, move: true) ?? throw new CheckFailedException("the split made no group");
        Check.True(groups.Groups[0].Tabs.Tabs.Count == 1 && right.Tabs.Tabs.Count == 1, "b moved to the new group");
        Check.True(ReferenceEquals(fixture.Host.ActiveDocument, right.Tabs.Active!.HostDocument), "the host's active document is the focused group's");

        Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.FocusGroupPrefix + "1"), "Ctrl+1 runs");
        Check.True(ReferenceEquals(groups.Focused, groups.Groups[0]) && ReferenceEquals(fixture.Host.ActiveDocument, a.HostDocument) && ReferenceEquals(shell.Tabs, groups.Groups[0].Tabs),
            "Ctrl+1 focuses the first group: its tabs are the shell's, its active document the host's (the agent's and the status bar's)");
        var opened = (await shell.OpenFileAsync(fixture.Write("c.txt", "c")))!;
        Check.True(groups.Groups[0].Tabs.Contains(opened), "quick open and Ctrl+O open in the focused group");

        Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.FocusGroupPrefix + "3"), "Ctrl+3 runs");
        Check.Equal(3, groups.Groups.Count, "Ctrl+3 with two groups makes a third to the right of the last, showing the focused tab");
        Check.True(ReferenceEquals(groups.Focused, groups.Groups[2]) && ReferenceEquals(groups.Focused.Tabs.Active!.Document, opened.Document), "focused, showing c");
        Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.SplitRight) && groups.Groups.Count == 3, "a fourth group is refused: CRAFT_EDITOR_GROUPS_MAX is 3");
        Check.True(fixture.Host.Notifications.Entries.Any(n => n.Text.Contains("CRAFT_EDITOR_GROUPS_MAX", StringComparison.Ordinal)), "and said, naming the setting");
        Check.True(groups.FocusNext() && ReferenceEquals(groups.Focused, groups.Groups[0]), "the next group after the last is the first");
    }

    [Test]
    public static async Task SplitDownMoveToNextDropsAndCloseGroupShapeTheLayout()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var a = (await shell.OpenFileAsync(fixture.Write("a.txt", "a")))!;
        var b = (await shell.OpenFileAsync(fixture.Write("b.txt", "b")))!;
        var groups = shell.Groups;
        var right = shell.Split(SplitDirection.Right)!;
        var down = shell.Split(SplitDirection.Down)!;
        Check.True(groups.Root is { Orientation: GroupOrientation.Row, Children: [{ Group: { } g1 }, { Orientation: GroupOrientation.Column, Children: [{ Group: { } g2 }, { Group: { } g3 }] }] }
            && ReferenceEquals(g2, right) && ReferenceEquals(g3, down), "a row: the first group, then a column of the right group over the new one");
        Check.True(Math.Abs(groups.Root.Weights[0] - 0.5) < 1e-9 && Math.Abs(groups.Root.Children[1].Weights[1] - 0.5) < 1e-9, "each split shares its room in halves");
        Check.SequenceEqual([1, 2, 3], groups.Groups.Select(groups.NumberOf).ToArray(), "numbered left to right, top to bottom");

        // Move to the next group: the tab and its view go, the source closes when left empty.
        var marker = new object();
        var moving = down.Tabs.Active!;
        moving.View = marker;
        Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.FocusGroupPrefix + "2"), "Ctrl+2");
        var tabInRight = right.Tabs.Active!;
        tabInRight.View = marker;
        Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.MoveToNextGroup), "Ctrl+Alt+Right runs");
        Check.True(!groups.Groups.Contains(right) && groups.Groups.Count == 2, "the group left empty closed");
        Check.True(ReferenceEquals(groups.Focused, down) && down.Tabs.Showing(b.Document) is { } kept && ReferenceEquals(kept, down.Tabs.Active),
            "the tab landed in the next group, which already showed b: its own tab is active there and focused");
        Check.True(groups.Root is { Orientation: GroupOrientation.Row, Children: [{ Group: { } }, { Group: { } last }] } && ReferenceEquals(last, down),
            "the column of one collapsed into the row");

        // Drop zones: the right third splits right, the bottom third splits down, the rest is the centre.
        Check.Equal(EditorDropZone.Centre, EditorGroups.ZoneAt(100, 100, 600, 400), "the centre");
        Check.Equal(EditorDropZone.Right, EditorGroups.ZoneAt(590, 150, 600, 400), "the right edge");
        Check.Equal(EditorDropZone.Down, EditorGroups.ZoneAt(200, 390, 600, 400), "the bottom edge");
        Check.Equal(EditorDropZone.Right, EditorGroups.ZoneAt(595, 300, 600, 400), "the corner goes to the nearer edge");
        Check.Equal(EditorDropZone.None, EditorGroups.ZoneAt(700, 100, 600, 400), "outside");
        var first = groups.Groups[0];
        var aTab = first.Tabs.Showing(a.Document)!;
        Check.True(groups.Drop(aTab, down, EditorDropZone.Centre) && down.Tabs.Showing(a.Document) is not null, "a tab dropped on a group's centre moves into it");
        Check.True(!groups.Drop(down.Tabs.Showing(a.Document)!, down, EditorDropZone.Centre), "onto its own group's centre: nothing");
        int before = groups.Groups.Count;
        Check.True(groups.Drop(down.Tabs.Showing(a.Document)!, down, EditorDropZone.Down) && groups.Groups.Count == before + 1, "a tab dropped on the bottom zone splits the group down");

        // Close the group: an edited document no other group shows asks first.
        var focused = groups.Focused;
        var edited = fixture.EditorOf(focused.Tabs.Active!);
        edited.OnText("x");
        fixture.Ui.Answer = UnsavedChoice.Cancel;
        Check.True(!await shell.CloseGroupAsync() && groups.Groups.Contains(focused), "kept open when the answer is cancel");
        fixture.Ui.Answer = UnsavedChoice.Discard;
        Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.CloseGroup) && !groups.Groups.Contains(focused), "View: Close the editor group closes it after the answer");
        Check.True(groups.Groups.Count >= 1 && groups.Groups.Contains(groups.Focused), "the focus went to a group still open");
    }

    [Test]
    public static async Task TheGroupCommandsAreInThePaletteTheViewMenuAndBothKeymaps()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var host = fixture.Host;
        (string Command, string Keys)[] expected =
        [
            (ShellCommandIds.SplitRight, "Ctrl+\\"), (ShellCommandIds.SplitDown, "Ctrl+K Ctrl+\\"), (ShellCommandIds.MoveToNextGroup, "Ctrl+Alt+Right"),
            (ShellCommandIds.FocusGroupPrefix + "1", "Ctrl+1"), (ShellCommandIds.FocusGroupPrefix + "2", "Ctrl+2"), (ShellCommandIds.FocusGroupPrefix + "3", "Ctrl+3"),
            (ShellCommandIds.PeekDefinition, "Alt+F12"),
        ];
        foreach (var keyMap in (string?[])[null, StandardShortcuts.VisualStudio])
        {
            host.SetKeyBindings([], keyMap);
            foreach (var (command, keys) in expected)
            {
                Check.Equal(keys, host.KeysOf(command), $"{command}'s keys in the {keyMap ?? "VS Code"} keymap");
            }
        }

        Check.True(EditorCommands.Find(ShellCommandIds.CloseGroup) is { Title: "View: Close the editor group" } && EditorCommands.Find(ShellCommandIds.FocusNextGroup) is not null,
            "Close group and Focus next group are in the palette (no default key)");
        var chord = KeyChord.Parse("Ctrl+\\");
        Check.True(chord.Key == EditorKey.Backslash && chord.DisplayText == "Ctrl+\\", "the backslash key parses and shows as \\");
        var view = ShellMenus.Rows(EditorMenus.View, host).Select(r => r.CommandId).ToArray();
        foreach (var id in (string[])[ShellCommandIds.SplitRight, ShellCommandIds.SplitDown, ShellCommandIds.MoveToNextGroup, ShellCommandIds.CloseGroup])
        {
            Check.True(view.Contains(id), $"the View menu has {id}");
        }
    }

    [Test]
    public static async Task TheLayoutIsKeptPerWorkspaceOnceSplitAndReadBack()
    {
        using var folder = new TempFolder();
        string layoutFile;
        {
            using var fixture = new SplitFixture(folder);
            var shell = await fixture.OpenWorkspaceAsync();
            var a = (await shell.OpenFileAsync(fixture.Write("src/a.txt", "a")))!;
            await shell.OpenFileAsync(fixture.Write("b.txt", "b"));
            layoutFile = shell.LayoutFile!.FilePath;
            await shell.LayoutSaved;
            Check.True(!File.Exists(layoutFile), "a single group writes no layout file into the workspace");
            var groups = shell.Groups;
            groups.Split(groups.Focused, SplitDirection.Right, a, move: true);
            shell.Split(SplitDirection.Down);
            groups.Resize(groups.Root, 0, 0.3);
            Check.True(groups.Focus(groups.Groups[0]), "focus back on the first group");
            await shell.LayoutSaved;
            Check.True(File.Exists(layoutFile), "the split layout is written to the workspace's .craft folder");
            var text = await File.ReadAllTextAsync(layoutFile);
            Check.True(text.Contains("\"src/a.txt\"", StringComparison.Ordinal) && text.Contains("\"column\"", StringComparison.Ordinal) && text.Contains("0.3", StringComparison.Ordinal),
                $"paths relative to the workspace, the column, the shares: {text}");
        }

        {
            using var fixture = new SplitFixture(folder);
            var shell = await fixture.OpenWorkspaceAsync();
            Check.True(await shell.RestoreEditorLayoutAsync(), "the kept layout is read back (off the UI thread)");
            var groups = shell.Groups;
            Check.Equal(3, groups.Groups.Count, "three groups");
            Check.True(groups.Root is { Orientation: GroupOrientation.Row, Children: [{ Group: { } }, { Orientation: GroupOrientation.Column }] }, "the same shape");
            Check.True(Math.Abs(groups.Root.Weights[0] - 0.3) < 1e-3, $"the same shares ({groups.Root.Weights[0]})");
            Check.Equal("b.txt", Path.GetFileName(groups.Groups[0].Tabs.Active?.FilePath), "the first group's file");
            Check.Equal("a.txt", Path.GetFileName(groups.Groups[1].Tabs.Active?.FilePath), "the second group's file");
            Check.True(ReferenceEquals(groups.Focused, groups.Groups[0]), "the focused group");
            Check.True(!await shell.RestoreEditorLayoutAsync(), "a split editor area is not shaped again");
        }
    }

    [Test]
    public static async Task TwoGroupsShowingA100000LineDocumentKeepOneCopyOfItsTextAndPaintOnlyTheirLines()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var text = new StringBuilder();
        for (int i = 0; i < 100_000; i++)
        {
            text.Append(CultureInfo.InvariantCulture, $"    var line{i} = Compute({i}, \"value\"); // line {i}\n");
        }

        var path = fixture.Write("big.grp", text.ToString());
        long textBytes = text.Length * 2L;
        var first = (await shell.OpenFileAsync(path))!;
        var left = fixture.EditorOf(first);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long heapBefore = GC.GetTotalMemory(forceFullCollection: true);
        long everywhere = GC.GetTotalAllocatedBytes(precise: true);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var group = shell.Split(SplitDirection.Right)!;
        long afterSplit = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var second = group.Tabs.Active!;
        var right = fixture.EditorOf(second);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        everywhere = GC.GetTotalAllocatedBytes(precise: true) - everywhere;
        long heapAfter = GC.GetTotalMemory(forceFullCollection: true);       // before the layout's file is written (its buffers are not the split's)
        await shell.LayoutSaved;
        Check.True(ReferenceEquals(first.Document, second.Document) && ReferenceEquals(left.LanguageSession, right.LanguageSession) && fixture.Language.Opened == 1,
            "one text document and one language session for both groups");
        Check.True(allocated < textBytes / 20 && everywhere < textBytes / 20, $"splitting ({afterSplit:N0} B) and the second view allocated {allocated:N0} B ({everywhere:N0} B on every thread), far under the text's {textBytes:N0} B: no second copy");
        Check.True(heapAfter - heapBefore < textBytes / 10, $"the heap grew {heapAfter - heapBefore:N0} B with the second group");

        // A warm frame per group: each draws its own place, laying out only its lines.
        using var leftRenderer = new EditorRenderer();
        using var rightRenderer = new EditorRenderer();
        using var leftCanvas = new LayoutCanvas { Record = false };
        using var rightCanvas = new LayoutCanvas { Record = false };
        float line = left.Metrics.LineHeight;
        float leftY = 1000 * line;
        float rightY = 80_000 * line;
        for (int i = 0; i < 200; i++)
        {
            leftRenderer.Draw(leftCanvas, left, 0, leftY, 600, 800, (i & 1) == 0);
            rightRenderer.Draw(rightCanvas, right, 0, rightY, 600, 800, (i & 1) == 0);
        }

        const int frames = 500;
        var leftTimes = new double[frames];
        var rightTimes = new double[frames];
        for (int i = 0; i < frames; i++)
        {
            long t = Stopwatch.GetTimestamp();
            leftRenderer.Draw(leftCanvas, left, 0, leftY, 600, 800, (i & 1) == 0);
            leftTimes[i] = Stopwatch.GetElapsedTime(t).TotalMicroseconds;
            t = Stopwatch.GetTimestamp();
            rightRenderer.Draw(rightCanvas, right, 0, rightY, 600, 800, (i & 1) == 0);
            rightTimes[i] = Stopwatch.GetElapsedTime(t).TotalMicroseconds;
        }

        Array.Sort(leftTimes);
        Array.Sort(rightTimes);
        int shown = (int)Math.Ceiling(800 / line) + 1;
        Check.True(leftRenderer.LinesDrawn <= shown + 1 && rightRenderer.LinesDrawn <= shown + 1, $"each group draws only its own lines ({leftRenderer.LinesDrawn} and {rightRenderer.LinesDrawn} of 100,000)");
        string result = string.Create(CultureInfo.InvariantCulture,
            $"Split editor, one 100,000-line document ({textBytes / (1024 * 1024)} MB of text) in two groups: the split allocated {afterSplit:N0} B, with the second view {allocated:N0} B ({everywhere:N0} B on every thread), the heap grew {heapAfter - heapBefore:N0} B (one text, one session); warm frame median {leftTimes[frames / 2]:F1} us (left, line 1,000) and {rightTimes[frames / 2]:F1} us (right, line 80,000), p99 {leftTimes[frames * 99 / 100]:F1} and {rightTimes[frames * 99 / 100]:F1} us, {leftRenderer.LinesDrawn} lines each");
        Console.WriteLine("    measured: " + result);
        var output = Path.Combine(TestRunner.RepositoryPath("test-output"), "benches");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "split-editor.txt"), result + Environment.NewLine);
        Check.True(leftTimes[frames / 2] < 5_000 && rightTimes[frames / 2] < 5_000, "a warm frame per group stays well under a frame");
    }
}

/// <summary>
/// The X restart's tree move by drag (piece 3a), headless: a file or folder dropped on a folder moves there off the UI
/// thread, its clean tabs following in every group, the undo bar saying so with Undo; Ctrl copies; the refusals said.
/// </summary>
[TestGroup("TreeMove")]
public static class TreeMoveTests
{
    [Test]
    public static async Task AFileDroppedOnAFolderMovesWithItsTabsInEveryGroupAndUndoMovesItBack()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var source = fixture.Write("src/a.txt", "alpha");
        fixture.Write("src/b.txt", "beta");
        Directory.CreateDirectory(Path.Combine(fixture.Workspace, "docs"));
        var a = (await shell.OpenFileAsync(source))!;
        await shell.OpenFileAsync(Path.Combine(fixture.Workspace, "src", "b.txt"));
        shell.Tabs.Activate(a);
        shell.Split(SplitDirection.Right);                       // a shown in both groups
        var activities = new List<string>();
        fixture.Host.ActivityCenter.Ended += (_, ended) => activities.Add(ended.Title);

        var docs = Path.Combine(fixture.Workspace, "docs");
        Check.Equal((string?)null, await shell.MoveEntryAsync(source, docs, copy: false), "the move is done");
        var moved = Path.Combine(docs, "a.txt");
        Check.True(File.Exists(moved) && !File.Exists(source), "the file is in docs, no longer in src");
        Check.True(activities.Contains("Moving a.txt"), $"the move ran as an activity ({string.Join(", ", activities)})");
        foreach (var group in shell.Groups.Groups)
        {
            Check.True(group.Tabs.Tabs.Any(t => string.Equals(t.FilePath, moved, StringComparison.Ordinal)) && !group.Tabs.Tabs.Any(t => string.Equals(t.FilePath, source, StringComparison.Ordinal)),
                $"group {shell.Groups.NumberOf(group)}'s tab follows the move");
        }

        Check.True(string.Equals(shell.Groups.Groups[0].Tabs.Tabs[0].FilePath, moved, StringComparison.Ordinal), "at its place in the first group");
        Check.Equal("Moved a.txt to docs", shell.UndoBar.Current?.Text, "the undo bar says what moved where");
        Check.True(shell.UndoBar.Undo(), "Undo");
        await WaitFor(() => File.Exists(source) && !File.Exists(moved) && shell.Groups.AllTabs.Any(t => string.Equals(t.FilePath, source, StringComparison.Ordinal)), "the file and its tabs back in src");
        Check.True(shell.UndoBar.Current is null, "the move back is not itself offered for undo");
    }

    [Test]
    public static async Task CtrlCopiesAndUndoDeletesTheCopy()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var source = fixture.Write("src/a.txt", "alpha");
        fixture.Write("src/inner/c.txt", "gamma");
        var docs = Path.Combine(fixture.Workspace, "docs");
        Directory.CreateDirectory(docs);
        Check.Equal((string?)null, await shell.MoveEntryAsync(source, docs, copy: true), "copied");
        Check.True(File.Exists(source) && File.Exists(Path.Combine(docs, "a.txt")), "both are there");
        Check.Equal("Copied a.txt to docs", shell.UndoBar.Current?.Text, "said with Undo");
        shell.UndoBar.Undo();
        await WaitFor(() => !File.Exists(Path.Combine(docs, "a.txt")), "Undo deletes the copy");
        Check.True(File.Exists(source), "the original stays");

        Check.Equal((string?)null, await shell.MoveEntryAsync(source, Path.Combine(fixture.Workspace, "src"), copy: true), "a copy onto its own folder");
        Check.True(File.Exists(Path.Combine(fixture.Workspace, "src", "a copy.txt")), "is named \"a copy.txt\"");
        Check.Equal((string?)null, await shell.MoveEntryAsync(Path.Combine(fixture.Workspace, "src", "inner"), docs, copy: true), "a folder copies with what it holds");
        Check.True(File.Exists(Path.Combine(docs, "inner", "c.txt")) && File.Exists(Path.Combine(fixture.Workspace, "src", "inner", "c.txt")), "both folders hold c.txt");
    }

    [Test]
    public static async Task RefusedMovesAreSaid()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var source = fixture.Write("src/a.txt", "alpha");
        fixture.Write("docs/a.txt", "other");
        fixture.Write("src/deep/x.txt", "x");
        var src = Path.Combine(fixture.Workspace, "src");
        var docs = Path.Combine(fixture.Workspace, "docs");

        var taken = await shell.MoveEntryAsync(source, docs, copy: false);
        Check.True(taken is not null && taken.Contains("has a a.txt already", StringComparison.Ordinal) && File.Exists(source), $"a name the folder has is refused: {taken}");
        var into = await shell.MoveEntryAsync(src, Path.Combine(src, "deep"), copy: false);
        Check.True(into is not null && into.Contains("cannot go into itself", StringComparison.Ordinal), $"a folder into itself is refused: {into}");

        shell.SameVolumeOf = (_, _) => false;                    // the other drive (Linux has one root here)
        var volume = await shell.MoveEntryAsync(Path.Combine(src, "deep"), docs, copy: false);
        Check.True(volume is not null && volume.Contains("another drive", StringComparison.Ordinal) && Directory.Exists(Path.Combine(src, "deep")), $"a folder to another drive is refused: {volume}");
        Check.True(fixture.Host.Notifications.Entries.Any(n => n.Text.Contains("another drive", StringComparison.Ordinal)), "and said in the status bar");
        Check.Equal((string?)null, await shell.MoveEntryAsync(Path.Combine(src, "deep"), docs, copy: true), "a copy to another drive is fine (Ctrl+drag)");
        shell.SameVolumeOf = ShellModel.SameVolume;

        var tab = (await shell.OpenFileAsync(source))!;
        fixture.EditorOf(tab).OnText("edited ");
        var dirty = await shell.MoveEntryAsync(source, Path.Combine(fixture.Workspace, "docs", "deep"), copy: false);
        Check.True(dirty is not null && dirty.Contains("unsaved changes", StringComparison.Ordinal) && File.Exists(source), $"a file with unsaved changes is not moved: {dirty}");
        Check.Equal((string?)null, await shell.MoveEntryAsync(source, src, copy: false), "dropped on its own folder: nothing to do");
    }

    internal static async Task WaitFor(Func<bool> done, string what)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            if (watch.Elapsed > TimeSpan.FromSeconds(10))
            {
                throw new CheckFailedException($"{what}: not within 10 s");
            }

            await Task.Delay(5);
        }
    }
}

/// <summary>
/// The X restart's peek definition (piece 3a), headless: Alt+F12 asks the language and raises the peek; an open document is
/// shared (its text and its session); a closed file is read off the calling thread with its syntactic colours only (no
/// workspace, nothing semantic); Escape closes, Enter opens it in the focused group with its name selected.
/// </summary>
[TestGroup("PeekDefinition")]
public static class PeekDefinitionTests
{
    [Test]
    public static async Task APeekOfAnOpenFileSharesItsDocumentAndItsSession()
    {
        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var target = (await shell.OpenFileAsync(fixture.Write("b.grp", "x\ny\nclass Target {}\n")))!;
        fixture.EditorOf(target);
        var source = (await shell.OpenFileAsync(fixture.Write("a.grp", "new Target();\n")))!;
        var model = fixture.EditorOf(source);
        int at = "x\ny\nclass ".Length;
        fixture.Language.Definition = new DefinitionLocation(target.FilePath, at, "Target".Length, IsSameDocument: false, "class Target");
        DefinitionLocation? asked = null;
        model.PeekRequested += (_, e) => asked = e.Location;
        model.Select(5, 5);
        Check.True(await fixture.Host.ExecuteCommandAsync(ShellCommandIds.PeekDefinition), "Alt+F12 runs");
        await TreeMoveTests.WaitFor(() =>
        {
            fixture.Pump();
            return asked is not null;
        }, "the definition comes back on the UI thread");
        Check.True(model.Selection.Caret == 5, "the caret does not move");

        using var peek = (await shell.PeekAsync(model, asked!))!;
        Check.True(ReferenceEquals(peek.Document, target.Document) && peek.IsOpenDocument && !peek.IsSyntacticOnly, "the open document is shared, not read again");
        Check.True(ReferenceEquals(peek.Session, fixture.EditorOf(target).LanguageSession) && shell.Sessions.UsesOf(target.Document) == 2 && fixture.Language.Opened == 2,
            "its session is the one its view holds (counted once more), none opened");
        Check.True(peek.Line == 2 && peek.Start == at && peek.Title == "b.grp · line 3", $"where: {peek.Title}");
        peek.Dispose();
        Check.True(shell.Sessions.UsesOf(target.Document) == 1 && fixture.Language.Disposed == 0, "closing the peek lets its hold go, the view's stays");
    }

    [Test]
    public static async Task APeekOfAClosedFileIsReadOffTheCallingThreadWithColoursOnly()
    {
        using var folder = new TempFolder();
        var gate = new ManualResetEventSlim(false);
        int reads = 0;
        using var fixture = new SplitFixture(folder, name => name == "CRAFT_PEEK_MAX_MB" ? "1" : null, async (path, options, token) =>
        {
            if (path.EndsWith(".grp", StringComparison.Ordinal) && Path.GetFileName(path).StartsWith("closed", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref reads);
                gate.Wait(TimeSpan.FromSeconds(5), token);
            }

            return await TextDocument.OpenAsync(path, options, token);
        });
        var shell = await fixture.OpenWorkspaceAsync();
        var closed = fixture.Write("closed.grp", "line one\nclass Closed {}\n");
        var source = (await shell.OpenFileAsync(fixture.Write("a.grp", "new Closed();\n")))!;
        var model = fixture.EditorOf(source);
        int opened = fixture.Language.Opened;

        var watch = Stopwatch.StartNew();
        var pending = shell.PeekAsync(model, new DefinitionLocation(closed, "line one\nclass ".Length, 6, false, "class Closed"));
        var returned = watch.Elapsed;
        Check.True(returned < TimeSpan.FromSeconds(1) && !pending.IsCompleted, $"the read waits off the calling thread (returned in {returned.TotalMilliseconds:F0} ms, not done)");
        gate.Set();
        using var peek = (await pending)!;
        Check.True(peek is { IsOpenDocument: false, IsSyntacticOnly: true, Line: 1 } && reads == 1, "the closed file was read once, for the peek");
        Check.True(peek.Session is not ISemanticLanguageSession && peek.Session is SyntacticLanguageSession { Inner: ISemanticLanguageSession }, "the language's syntactic layer only: nothing semantic is reachable");
        var context = fixture.Language.Contexts[^1];
        Check.True(fixture.Language.Opened == opened + 1 && context.WorkspaceRoot is null && context.Settings is { SemanticMaxCharacters: 1, SemanticColorsDelayMilliseconds: -1 },
            "opened with no workspace, its semantic layer deferred and semantic colours off");
        Check.True(shell.Groups.FindOpen(closed) is null, "no tab opened");
        peek.Dispose();
        Check.True(fixture.Language.Disposed == 1, "closing the peek disposes its own session");

        var big = fixture.Write("large.txt", new string('x', 2 * 1024 * 1024));
        Check.True(await shell.PeekAsync(model, new DefinitionLocation(big, 0, 1, false, "x")) is null, "a file over CRAFT_PEEK_MAX_MB is not read");
        Check.True(fixture.Host.Notifications.Entries.Any(n => n.Text.Contains("CRAFT_PEEK_MAX_MB", StringComparison.Ordinal)), "said, naming the setting");
    }

    [Test]
    public static async Task EscapeClosesAndEnterOpensTheDefinitionInTheFocusedGroup()
    {
        Check.Equal(PeekAction.Close, PeekSession.ActionFor(EditorKey.Escape, KeyModifiers.None), "Escape closes");
        Check.Equal(PeekAction.Open, PeekSession.ActionFor(EditorKey.Enter, KeyModifiers.None), "Enter opens");
        Check.Equal(PeekAction.None, PeekSession.ActionFor(EditorKey.Down, KeyModifiers.None), "the arrows move in the peek's editor");
        Check.Equal(PeekAction.None, PeekSession.ActionFor(EditorKey.Enter, KeyModifiers.Shift), "Shift+Enter is not the peek's");

        using var folder = new TempFolder();
        using var fixture = new SplitFixture(folder);
        var shell = await fixture.OpenWorkspaceAsync();
        var target = fixture.Write("t.grp", "a\nb\nc\nvoid Target() {}\n");
        var source = (await shell.OpenFileAsync(fixture.Write("a.grp", "Target();\n")))!;
        shell.Split(SplitDirection.Right);
        var model = fixture.EditorOf(shell.Tabs.Active!);
        int start = "a\nb\nc\nvoid ".Length;
        var peek = (await shell.PeekAsync(model, new DefinitionLocation(target, start, 6, false, "void Target()")))!;
        var opened = await shell.OpenPeekAsync(peek);
        peek.Dispose();
        Check.True(opened is not null && shell.Groups.Groups[1].Tabs.Contains(opened) && ReferenceEquals(shell.Groups.Focused, shell.Groups.Groups[1]),
            "Enter opens the definition in the focused (second) group");
        var editor = fixture.EditorOf(opened!);
        Check.True(editor.Selection is { Anchor: var anchor, Caret: var caret } && anchor == start + 6 && caret == start, "its name selected");

        // A definition in the document itself: the peek shows it, Enter moves the caret there.
        var own = (await shell.PeekAsync(model, new DefinitionLocation(source.FilePath, 0, 6, true, "here")))!;
        Check.True(ReferenceEquals(own.Document, model.Document) && own.IsOpenDocument, "the source's own document");
        shell.Tabs.Activate(shell.Groups.Focused.Tabs.Showing(model.Document)!);
        await shell.OpenPeekAsync(own);
        own.Dispose();
        Check.True(model.Selection.Caret == 0 && model.Selection.Anchor == 6, "Enter selects it in the editor it came from");

        // A metadata symbol: no peek, its description as a tooltip.
        fixture.Language.Definition = new DefinitionLocation(null, 0, 0, false, "class System.Console");
        bool raised = false;
        model.PeekRequested += (_, _) => raised = true;
        Check.True(model.PeekDefinition(), "asked");
        await TreeMoveTests.WaitFor(() =>
        {
            fixture.Pump();
            return model.Tooltip is not null;
        }, "the metadata symbol's description");
        Check.True(!raised && model.Tooltip?.Text == "class System.Console", "a metadata symbol shows its description, no peek");
    }
}
