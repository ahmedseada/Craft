// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using Craft.Abstractions;
using Craft.Ai;
using Craft.Core;
using Craft.Core.Documents;
using Craft.Core.Plugins;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Plugins.Git;
using Craft.Tests.Editor;
using Craft.Tests.Runner;
using Idrak.Abstraction;
using Idrak.Abstraction.Generation;
using Idrak.Abstraction.Plugins;
using Idrak.Text.Generation;

namespace Craft.Tests;

/// <summary>
/// X part 8: the Source Control view, a pre-installed, removable git plug-in (plugins/Craft.Plugins.Git) on the user's own
/// git, with the gutter's marks, the rail's count, the status bar's branch and "Generate commit message" through
/// <see cref="CommitMessageProviders"/>. The real repository tests need git on the machine; without it they say so and end.
/// </summary>
[TestGroup("SourceControl")]
public static class SourceControlTests
{
    // ---- parsing (bytes) ----

    [Test]
    public static void PorcelainV2FixturesParseRenamesConflictsUntrackedAndSpaces()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "repo"));
        var output = Encoding.UTF8.GetBytes(string.Join('\0',
            "# branch.oid 1234567890abcdef1234567890abcdef12345678",
            "# branch.head main",
            "# branch.upstream origin/main",
            "# branch.ab +2 -1",
            "# stash 3",
            "1 .M N... 100644 100644 100644 aaaa bbbb src/A.cs",
            "1 M. N... 100644 100644 100644 aaaa bbbb src/B.cs",
            "1 MM N... 100644 100644 100644 aaaa bbbb both.cs",
            "1 A. N... 000000 100644 100644 0000 cccc new staged.cs",
            "1 .D N... 100644 100644 000000 aaaa aaaa gone.cs",
            "2 R. N... 100644 100644 100644 aaaa aaaa R100 docs/Moved file.md", "docs/Old file.md",
            "u UU N... 100644 100644 100644 100644 aaaa bbbb cccc conflict.cs",
            "? notes with spaces.txt",
            "? dir/ünïcode.txt",
            "! bin/ignored.dll") + "\0");
        var status = GitParsers.Status(root, output);
        Check.Equal("main", status.Branch, "the branch from branch.head");
        Check.True(status is { Upstream: "origin/main", Ahead: 2, Behind: 1, Stashes: 3, Detached: false }, $"upstream, ahead and behind, stashes ({status.Upstream} +{status.Ahead} -{status.Behind}, {status.Stashes})");
        Check.Equal("main ↑2 ↓1", status.BranchText, "the status bar's branch with ahead and behind");
        Check.SequenceEqual(["src/B.cs", "both.cs", "new staged.cs", "docs/Moved file.md"], [.. status.Staged.Select(f => f.RelativePath)], "the staged entries (X), in order");
        Check.SequenceEqual(["src/A.cs", "both.cs", "gone.cs", "notes with spaces.txt", "dir/ünïcode.txt"], [.. status.Changes.Select(f => f.RelativePath)], "the working copy's (Y), then the untracked, ignored left out");
        var renamed = status.Staged.Single(f => f.State == SourceControlState.Renamed);
        Check.Equal(Path.Combine(root, "docs", "Old file.md"), renamed.OldPath, "a rename's old path after its NUL, spaces kept");
        Check.Equal(Path.Combine(root, "docs", "Moved file.md"), renamed.Path, "its new path, full");
        Check.Equal("R", renamed.Letter, "its letter");
        Check.True(status.Changes.Single(f => f.RelativePath == "gone.cs").State == SourceControlState.Deleted, "a deletion in the working copy");
        Check.True(status.Staged.Single(f => f.RelativePath == "new staged.cs").State == SourceControlState.Added, "an added file with a space");
        Check.True(status.Conflicts is [{ State: SourceControlState.Conflict, RelativePath: "conflict.cs" }], "the unmerged entry is a conflict");
        Check.True(status.Changes.Count(f => f.State == SourceControlState.Untracked) == 2, "two untracked files, one in a folder, unicode kept");
        Check.Equal(9, status.Count, "the badge counts each file once (both.cs staged and changed is one)");
        Check.True(status.FileState(Path.Combine(root, "both.cs")) is { Staged: false, State: SourceControlState.Modified }, "the tree shows the working copy's change first");
        Check.Equal(PanelTone.Success, SourceControlStates.Tone(SourceControlState.Untracked), "untracked in the success colour");
        Check.Equal(PanelTone.Error, SourceControlStates.Tone(SourceControlState.Deleted), "deleted in the error colour");

        var detached = GitParsers.Status(root, "# branch.oid 0123456789abcdef\0# branch.head (detached)\0"u8);
        Check.True(detached is { Detached: true, Branch: "01234567", Upstream: null, Count: 0 }, $"a detached HEAD shows its short commit ({detached.Branch})");
        var initial = GitParsers.Status(root, "# branch.oid (initial)\0# branch.head main\0"u8);
        Check.True(initial is { Branch: "main", Count: 0 }, "a repository with no commit yet");
    }

    [Test]
    public static void BranchesHunksAndConflictBlocksParseAsBytes()
    {
        var branches = GitParsers.Branches("feature/x\torigin/feature/x\t[ahead 3, behind 1]\nmain\torigin/main\t\nlocal\t\t\n"u8);
        Check.SequenceEqual(["feature/x", "main", "local"], [.. branches.Select(b => b.Name)], "every local branch");
        Check.True(branches[0] is { Upstream: "origin/feature/x", Ahead: 3, Behind: 1 }, "ahead and behind from the track");
        Check.True(branches[1] is { Upstream: "origin/main", Ahead: 0, Behind: 0 } && branches[2].Upstream is null, "in step, and no upstream");

        var hunks = GitParsers.Hunks("diff --git a/a.cs b/a.cs\n--- a/a.cs\n+++ b/a.cs\n@@ -2 +2 @@ class A\n-x\n+y\n@@ -5,0 +6,2 @@\n+n1\n+n2\n@@ -9,2 +10,0 @@\n-gone1\n-gone2\n"u8);
        Check.SequenceEqual([(2, 1, 2, 1), (5, 0, 6, 2), (9, 2, 10, 0)], [.. hunks], "each @@ line's numbers (a count left out is 1)");
        var marks = GitParsers.Marks(hunks);
        Check.SequenceEqual([new LineMark(1, 1, LineMarkKind.Changed), new LineMark(5, 2, LineMarkKind.Added), new LineMark(10, 2, LineMarkKind.Deleted)], [.. marks],
            "changed line 2, added lines 6-7, two removed before line 11 (0-based)");

        var diff = TextDiff.OfTexts("one\ntwo\nthree\nfour\n", "one\nTWO\nthree\nfour\nfive\n", 0);
        Check.SequenceEqual([new LineMark(1, 1, LineMarkKind.Changed), new LineMark(4, 1, LineMarkKind.Added)], [.. GitParsers.Marks(diff)], "the same marks from Craft's own diff");
        var removed = TextDiff.OfTexts("one\ntwo\nthree\n", "one\nthree\n", 0);
        Check.SequenceEqual([new LineMark(1, 1, LineMarkKind.Deleted)], [.. GitParsers.Marks(removed)], "a removed line: a wedge before the line after it");

        const string Text = "a\n<<<<<<< HEAD\nmine\n=======\ntheirs\n>>>>>>> other\nb\n<<<<<<< HEAD\nm2\n||||||| base\nbase\n=======\nt2\n>>>>>>> other\n";
        var blocks = GitParsers.Conflicts(Text);
        Check.SequenceEqual([new ConflictBlock(1, -1, 3, 5), new ConflictBlock(7, 9, 11, 13)], [.. blocks], "two conflicts, the second with a diff3 base");
        Check.Equal("a\nmine\nb\nm2\n", GitSession.Resolve(Text, blocks, GitSession.ConflictChoice.Current), "accept current keeps ours, drops the base");
        Check.Equal("a\ntheirs\nb\nt2\n", GitSession.Resolve(Text, blocks, GitSession.ConflictChoice.Incoming), "accept incoming");
        Check.Equal("a\nmine\ntheirs\nb\nm2\nt2\n", GitSession.Resolve(Text, blocks, GitSession.ConflictChoice.Both), "accept both, ours first");
        Check.Equal("a\nmine\nb\n<<<<<<< HEAD\nm2\n||||||| base\nbase\n=======\nt2\n>>>>>>> other\n", GitSession.Resolve(Text, blocks, GitSession.ConflictChoice.Current, blocks[0]), "one conflict alone");
    }

    // ---- the editor: marks in the gutter and on the ruler, a click, the inline card ----

    [Test]
    public static void GutterMarksFromAFixtureDiffAreDrawnAndAClickShowsTheCardWithItsActions()
    {
        using var document = TextDocument.Create(string.Concat(Enumerable.Range(0, 30).Select(i => $"line {i}\n")));
        using var model = new EditorModel(document, null, new CraftOptions(), null, new FixedMeasurer(), new FakeClipboard()) { Dispatch = action => action() };
        using var other = new EditorModel(document, null, new CraftOptions(), null, new FixedMeasurer(), new FakeClipboard()) { Dispatch = action => action() };
        using var renderer = new EditorRenderer();
        var canvas = new RecordingCanvas();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        long rulerBefore = renderer.OverviewRuler.Rebuilds;
        Check.Equal(0, renderer.LineMarksDrawn, "no marks yet");

        var marks = DocumentLineMarks.Of(document);
        int invalidated = 0;
        model.Invalidated += (_, _) => invalidated++;
        var fixture = GitParsers.Marks(GitParsers.Hunks("@@ -2 +2 @@\n-x\n+y\n@@ -5,0 +6,2 @@\n+a\n+b\n@@ -9,2 +10,0 @@\n-c\n-d\n"u8));
        marks.Set("git", fixture);
        Check.True(invalidated > 0, "new marks redraw the editor");
        Check.Equal(3, model.LineMarks.Length, "both editors of the document see the marks");
        Check.Equal(3, other.LineMarks.Length, "the split editor too");
        canvas.Clear();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        Check.Equal(3, renderer.LineMarksDrawn, "three marks drawn on the visible lines");
        var colors = model.Theme.Colors;
        float gutter = model.GutterWidth;
        Check.True(canvas.Calls.Any(c => c.Kind == "fill" && c.Argb == colors.Info && c.Y == 20 && c.X < gutter), "the changed line 2 in the info colour, in the gutter");
        Check.True(canvas.Calls.Count(c => c.Kind == "fill" && c.Argb == colors.Success && c.X < gutter && c.H == 20) == 2, "the two added lines in the success colour");
        Check.True(canvas.Calls.Any(c => c.Kind == "fill" && c.Argb == colors.Error && c.X < gutter && c.H < 20), "the removed lines as a wedge in the error colour");
        Check.True(renderer.OverviewRuler.Rebuilds > rulerBefore, "the ruler bucketed the marks again");
        Check.True(canvas.Calls.Any(c => c.Kind == "fill" && c.Argb == colors.Success && c.X >= 800 - model.OverviewRulerWidth), "the ruler shows them at its left edge");

        // A click on a mark's strip: its source hears it; it shows the change inline in the editor clicked.
        LineMarkClickEventArgs? clicked = null;
        marks.Clicked += (_, e) => clicked = e;
        model.PointerPressed(gutter - 3, (5 * 20) + 10, KeyModifiers.None);
        Check.True(clicked is { Source: "git", Mark.Kind: LineMarkKind.Added, Mark.Line: 5 } && ReferenceEquals(clicked.Editor, model), $"the click names the added mark of line 6 ({clicked?.Mark})");
        int reverted = 0, staged = 0;
        marks.ShowCard(new InlineCard(6, "- old line\n+ line 5\n+ line 6", [new InlineCardAction("Revert", () => reverted++), new InlineCardAction("Stage", () => staged++)]));
        Check.True(model.Tooltip is { Kind: EditorTooltipKind.Card, Actions.Count: 2 }, "the editor clicked shows the card");
        Check.True(other.Tooltip is null, "the other editor of the document does not");
        canvas.Clear();
        renderer.Draw(canvas, model, 0, 0, 800, 400, caretVisible: false);
        Check.True(canvas.Texts.Any(t => t.Text == "Revert" && t.Argb == colors.Info) && canvas.Texts.Any(t => t.Text == "Stage"), "its actions drawn as a row of links");
        Check.True(canvas.Calls.Any(c => c.Kind == "fill" && c.Argb == colors.DiffRemoved) && canvas.Calls.Any(c => c.Kind == "fill" && c.Argb == colors.DiffAdded), "its removed and added lines on the diff's fills");
        model.PointerHovered(5, 395);
        model.PointerExited();
        Check.True(model.Tooltip is { Kind: EditorTooltipKind.Card }, "the card stays when the pointer leaves (until Escape, a press outside, or an action)");
        var box = model.HoverCard.Layout()!.Value;
        Check.True(box.Y >= 6 * 20, "under its line");
        float actionsY = box.Y + box.Height - 10;
        float stageX = -1;
        for (float x = box.X; x < box.X + box.Width; x += 2)
        {
            if (model.HoverCard.ActionAt(x, actionsY) == 1)
            {
                stageX = x;
                break;
            }
        }

        Check.True(stageX > 0, "Stage has its place on the actions' row");
        model.PointerPressed(stageX, actionsY, KeyModifiers.None);
        Check.True(staged == 1 && reverted == 0 && model.Tooltip is null, "a press on Stage runs it, and the card closes");
        marks.ShowCard(new InlineCard(6, "- x", [new InlineCardAction("Revert", () => reverted++)]));
        model.OnKey(EditorKey.Escape, KeyModifiers.None);
        Check.True(model.Tooltip is null && reverted == 0, "Escape closes the card, running nothing");

        marks.Set("git", []);
        Check.Equal(0, model.LineMarks.Length, "an empty list takes the marks away");
    }

    // ---- the view and the shell ----

    [Test]
    public static async Task TheViewGroupsTheFilesTheRailBadgeCountsThemAndTheShellShowsTheBranch()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "repo"));
        var status = GitParsers.Status(root, Encoding.UTF8.GetBytes(
            "# branch.head main\0# branch.upstream origin/main\0# branch.ab +1 -0\0" +
            "1 M. N... 100644 100644 100644 a b src/Staged.cs\0" +
            "1 .D N... 100644 100644 000000 a a old.txt\0" +
            "? new.json\0"));
        var state = new SourceControlViewState { Status = status, Branches = [new GitBranchInfo("main", "origin/main", 1, 0), new GitBranchInfo("dev", null, 0, 0)] };
        var blocks = SourceControlView.Blocks(state);
        var lists = blocks.OfType<PanelFileList>().ToList();
        Check.SequenceEqual(["staged", "changes"], [.. lists.Select(l => l.Id)], "Staged changes, then Changes (no conflicts: no group)");
        Check.True(lists[0].Rows is [{ Name: "Staged.cs", Folder: "src", Letter: "M", Tone: PanelTone.Warning }], "a staged file: its name, folder, letter and colour");
        Check.True(lists[1].Rows.Any(r => r is { Name: "old.txt", Letter: "D", Struck: true, Tone: PanelTone.Error }), "a deleted file struck through in the error colour");
        Check.True(lists[1].Rows.Any(r => r is { Name: "new.json", Letter: "U", Tone: PanelTone.Success }), "an untracked file in the success colour");
        Check.True(lists[1].Rows.All(r => r.ActionId == SourceControlView.Open && r.Actions.Any(a => a.ActionId == SourceControlView.Stage) && r.Actions.Any(a => a.ActionId == SourceControlView.Discard)),
            "a change opens its diff on click, and stages or discards from its row");
        Check.True(lists[1].Actions.Any(a => a.ActionId == SourceControlView.StageAll) && lists[0].Actions.Any(a => a.ActionId == SourceControlView.UnstageAll), "stage all and unstage all on the groups");
        var box = blocks.OfType<PanelMessageBox>().Single();
        Check.True(box is { SubmitActionId: SourceControlView.Commit, SubmitKeys: "Ctrl+Enter" } && box.Placeholder!.Contains("on main", StringComparison.Ordinal), "the commit box commits on Ctrl+Enter");
        Check.True(box.Actions is [{ ActionId: SourceControlView.Generate, Icon: SourceControlView.SparkleIcon }], "the sparkle button in the box");
        var picker = blocks.OfType<PanelPicker>().Single();
        Check.True(picker is { Selected: "main" } && picker.Options.Count == 2 && picker.Options[0].Detail!.Contains("↑1", StringComparison.Ordinal), "the branch with ahead and behind");
        var sync = blocks.OfType<PanelButtons>().SelectMany(b => b.Buttons).ToList();
        Check.True(sync.Any(b => b.Title == "Push ↑1") && sync.Any(b => b.ActionId == SourceControlView.Pull) && sync.Any(b => b.ActionId == SourceControlView.Sync)
            && sync.Any(b => b.ActionId == SourceControlView.Fetch) && sync.Any(b => b.ActionId == SourceControlView.Stash) && sync.Any(b => b.ActionId == SourceControlView.Pop && !b.Enabled),
            "Pull, Push with its count, Sync, Fetch, Stash, and Pop (none to pop)");
        Check.Equal("3", SourceControlView.Badge(state), "the rail's badge: three changed files");
        Check.True(SourceControlView.Badge(state with { Status = SourceControlStatus.None }) is null, "no change: no badge");
        var none = SourceControlView.Blocks(new SourceControlViewState { Unavailable = "Git was not found on PATH. Install git…" });
        Check.True(none is [PanelCard { Title: "Git is not available" } card] && PanelBlocks.Describe(card).Contains("Try again", StringComparison.Ordinal), "no git: the view says so, with what to do");

        // The shell: the badge on the panel's rail item, the repository's branch and the tree's file states.
        using var folder = new TempFolder();
        var settings = new CraftSettings(_ => null, SettingsFile.Load(folder.File("data/settings.json")));
        using var host = new EditorHost();
        using var shell = new ShellModel(host, settings, RecentList.Load(folder.File("data/recent.json"), 10), new NoDialogs(), new SilentUi());
        Check.True(await shell.OpenFolderAsync(folder.Path), "the folder opens");
        var panel = new ViewPanel(SourceControlView.PanelId, "Source Control", (_, _, _) => Task.CompletedTask);
        int badgeChanges = 0;
        shell.RailBadgesChanged += (_, _) => badgeChanges++;
        using (host.AddPanel(panel))
        {
            Check.True(shell.RailBadge(SourceControlView.PanelId) is null, "no badge yet");
            panel.Badge = "3";
            Check.Equal("3", shell.RailBadge(SourceControlView.PanelId), "the panel's count is its rail item's badge");
            int seen = badgeChanges;
            panel.SetBlocks([new PanelText("redrawn")]);
            Check.Equal(seen, badgeChanges, "a redraw that keeps the count does not rebuild the rail");
            panel.Badge = null;
            Check.True(shell.RailBadge(SourceControlView.PanelId) is null && badgeChanges == seen + 1, "no change: the badge goes");
        }

        var repository = new FakeRepository(folder.Path) { Status = new SourceControlStatus(folder.Path, "dev") { Ahead = 2, Changes = [new SourceControlFile(Path.Combine(folder.Path, "a.cs"), SourceControlState.Modified, false)] } };
        int changed = 0;
        shell.SourceControlChanged += (_, _) => changed++;
        using (host.AddRepository(repository))
        {
            Check.Equal("dev ↑2", shell.BranchText, "the status bar's branch is the repository's, with ahead");
            Check.True(shell.FileState(Path.Combine(folder.Path, "a.cs")) is { Letter: "M" } && shell.FileState(Path.Combine(folder.Path, "b.cs")) is null, "the tree's state of a file");
            repository.Raise();
            Check.True(changed >= 2, "published and changed: the shell hears both");
        }

        Check.True(shell.SourceControl is null, "taken back: no repository");
    }

    // ---- a real repository ----

    [Test]
    public static void ARealRepositoryStagesUnstagesCommitsDiscardsWithUndoAndSwitchesBranch()
    {
        if (Repo.Missing() is { } why)
        {
            Console.WriteLine($"  skipped: {why}");
            return;
        }

        using var repo = new Repo();
        var session = repo.Session;
        repo.Until(() => session.Repository?.Status.Branch == "main" && session.State.Status.Changes.Count == 2, "the repository opens with its two changes");
        string a = repo.File("a.txt"), spaced = repo.File("new file.txt");
        Check.True(session.State.Status.Changes.Any(f => f.Path == a && f.State == SourceControlState.Modified) && session.State.Status.Changes.Any(f => f.Path == spaced && f.State == SourceControlState.Untracked),
            "a.txt modified, \"new file.txt\" untracked");
        Check.Equal("2", session.Panel.Badge, "the rail's badge counts them");
        Check.True(repo.Host.Repositories.Count == 1 && repo.Host.Repositories[0].Root == repo.Root, "the repository is published to the shell");

        repo.Act(SourceControlView.Stage, a);
        repo.Until(() => session.State.Status.Staged.Any(f => f.Path == a), "stage moves a.txt to the staged changes");
        Check.True(repo.Activities.Any(t => t.StartsWith("Staging", StringComparison.Ordinal)), "the action is an activity");
        repo.Act(SourceControlView.Unstage, a);
        repo.Until(() => session.State.Status.Staged.Count == 0 && session.State.Status.Changes.Any(f => f.Path == a), "unstage takes it back to the changes");

        repo.Act(SourceControlView.Stage, a);
        repo.Until(() => session.State.Status.Staged.Count == 1, "staged again");
        repo.Act(SourceControlView.Message, "Change a");
        int version = session.State.MessageVersion;
        repo.Act(SourceControlView.Commit, null);
        repo.Until(() => repo.Git("log", "-1", "--format=%s") == "Change a", "Commit commits the staged change with the message");
        repo.Until(() => session.State.MessageVersion > version && session.Draft.Length == 0, "the box is emptied after the commit");
        Check.True(session.State.Status.Changes.Any(f => f.Path == spaced), "the untracked file stays");

        // Nothing staged: commit asks to stage everything first (the presenter answers "Stage all and commit").
        File.WriteAllText(a, "one\nTWO\nthree\n");
        repo.Session.Repository!.ScheduleRefresh();
        repo.Until(() => session.State.Status.Changes.Any(f => f.Path == a), "the change is seen");
        repo.Act(SourceControlView.Message, "Everything");
        repo.Act(SourceControlView.Commit, null);
        repo.Until(() => repo.Git("log", "-1", "--format=%s") == "Everything", "commit with nothing staged asked, staged all and committed");
        Check.True(repo.Presenter.Questions.Any(q => q.Contains("Nothing is staged", StringComparison.Ordinal)), "it asked first");
        Check.Equal("", repo.Git("status", "--porcelain"), "every change was committed");

        // Discard with Undo: the change goes at once, the shell's bar takes it back ("new file.txt" is tracked now; a new one is not).
        string untracked = repo.File("untracked one.txt");
        File.WriteAllText(a, "changed\n");
        File.WriteAllText(untracked, "kept\n");
        session.Repository!.ScheduleRefresh();
        repo.Until(() => session.State.Status.Changes.Count == 2, "two changes again");
        repo.Act(SourceControlView.DiscardAll, null);
        repo.Until(() => session.State.Status.Changes.Count == 0 && repo.Undo is not null, "Discard all leaves no change, and the Undo bar shows");
        Check.Equal("one\nTWO\nthree\n", File.ReadAllText(a), "the tracked file is back to its committed text");
        Check.True(!File.Exists(untracked), "the untracked file is gone");
        Check.True(repo.Undo is { Text: var said } && said.Contains("2 files", StringComparison.Ordinal), $"the Undo bar says what was discarded ({repo.Undo?.Text})");
        repo.Undo!.Value.Run();
        repo.Until(() => session.State.Status.Changes.Count == 2, "Undo puts both back");
        Check.Equal("changed\n", File.ReadAllText(a), "the discarded text is back");
        Check.Equal("kept\n", File.ReadAllText(untracked), "and the deleted untracked file");

        // Branches: create one (switching to it), then switch back.
        repo.Act(SourceControlView.NewBranch, "feature/x");
        repo.Until(() => session.State.Status.Branch == "feature/x", "a new branch is made and checked out");
        repo.Until(() => session.State.Branches.Any(b => b.Name == "feature/x") && session.State.Branches.Any(b => b.Name == "main"), "the picker lists both branches");
        repo.Act(SourceControlView.Branch, "main");
        repo.Until(() => session.State.Status.Branch == "main", "switching back to main");
        Check.Equal("main", repo.Git("rev-parse", "--abbrev-ref", "HEAD"), "git agrees");
    }

    [Test]
    public static void TheGutterOfAnOpenFileMarksItsChangesAgainstTheIndexAndAHunkStagesAlone()
    {
        if (Repo.Missing() is { } why)
        {
            Console.WriteLine($"  skipped: {why}");
            return;
        }

        using var repo = new Repo();
        var session = repo.Session;
        repo.Until(() => session.Repository?.Status.Branch == "main", "the repository opens");
        string a = repo.File("a.txt");
        repo.Git("checkout", "--", "a.txt");                       // the committed text: one, two, three
        using var document = repo.Wait(TextDocument.OpenAsync(a));
        var open = repo.Host.Open(document, null);
        repo.Until(() => session.GutterOf(open).IsCompleted, "the opened file's marks are worked out");
        Check.Equal(0, session.MarksOf(open).Count, "the file as committed: no marks");

        var snapshot = document.Buffer.Current;
        int two = snapshot.GetLine(1).Start;
        document.Edit([new TextEdit(two, 3, "TWO"), new TextEdit(snapshot.Length, 0, "four\n")], EditKind.Typing, SelectionState.At(two), SelectionState.At(two));
        repo.Until(() => session.MarksOf(open).Count == 2, "typing marks the lines after the pause, off the UI thread");
        Check.SequenceEqual([new LineMark(1, 1, LineMarkKind.Changed), new LineMark(3, 1, LineMarkKind.Added)], [.. session.MarksOf(open)], "line 2 changed, line 4 added, against the index");
        Check.Equal(2, DocumentLineMarks.Of(document).Marks.Length, "the host's editors draw them");

        // A click on the changed line's mark shows the change inline; its Stage puts that hunk alone in the index.
        using var model = new EditorModel(document, null, new CraftOptions(), null, new FixedMeasurer(), new FakeClipboard()) { Dispatch = action => action() };
        using (var renderer = new EditorRenderer())
        {
            renderer.Draw(new RecordingCanvas(), model, 0, 0, 800, 400, caretVisible: false);
        }

        model.PointerPressed(model.GutterWidth - 3, 20 + 10, KeyModifiers.None);
        repo.Pump();
        Check.True(model.Tooltip is { Kind: EditorTooltipKind.Card, Actions: [{ Title: "Revert" }, { Title: "Stage" }] } card && card.Text.Contains("- two", StringComparison.Ordinal) && card.Text.Contains("+ TWO", StringComparison.Ordinal),
            $"the change shows inline with Revert and Stage ({model.Tooltip?.Text})");
        model.Tooltip!.Value.Actions![1].Run();
        repo.Until(() => repo.Git("show", ":a.txt") == "one\nTWO\nthree", "Stage put the hunk in the index");
        Check.True(repo.Git("diff", "--cached").Contains("+TWO", StringComparison.Ordinal) && !repo.Git("diff", "--cached").Contains("four", StringComparison.Ordinal), "only that hunk: the added line stays unstaged");
        repo.Until(() => session.MarksOf(open) is [{ Kind: LineMarkKind.Added, Line: 3 }], "the gutter now marks the added line only");

        // Revert goes through the diff (one undo step the user accepts): the presenter accepts it.
        model.PointerPressed(model.GutterWidth - 3, (3 * 20) + 10, KeyModifiers.None);
        repo.Pump();
        model.Tooltip!.Value.Actions![0].Run();
        repo.Until(() => document.Buffer.Current.GetText(0, document.Buffer.Current.Length) == "one\nTWO\nthree\n", "Revert takes the added line back");
        Check.True(repo.Presenter.Proposals.Any(t => t.Contains("Revert this change", StringComparison.Ordinal)), "through a proposal the user accepted");
        repo.Host.Close(open);
    }

    // ---- generate the commit message ----

    [Test]
    public static void TheBudgetTrimsTheDiffByFileTheLargestFirst()
    {
        string File(string name, int lines) => $"diff --git a/{name} b/{name}\n--- a/{name}\n+++ b/{name}\n@@ -1 +1,{lines} @@\n" + string.Concat(Enumerable.Range(0, lines).Select(i => $"+line {i} of {name}\n"));
        string diff = File("small.cs", 2) + File("big.cs", 200) + File("medium.cs", 20);
        var whole = CommitMessages.Trim(Encoding.UTF8.GetBytes(diff), 100_000);
        Check.True(whole is { Files: 3, Summarised: 0 } && whole.Text == diff, "within the budget: the diff as it is");
        var trimmed = CommitMessages.Trim(Encoding.UTF8.GetBytes(diff), 300);
        Check.Equal(1, trimmed.Summarised, "the largest file alone is summarised");
        Check.True(trimmed.Text.Contains("big.cs: +200 −0 lines, summarised", StringComparison.Ordinal) && !trimmed.Text.Contains("line 5 of big.cs", StringComparison.Ordinal), "by name and counts");
        Check.True(trimmed.Text.IndexOf("small.cs", StringComparison.Ordinal) < trimmed.Text.IndexOf("big.cs", StringComparison.Ordinal)
            && trimmed.Text.IndexOf("big.cs", StringComparison.Ordinal) < trimmed.Text.IndexOf("medium.cs", StringComparison.Ordinal), "the files keep their order");
        Check.True(trimmed.Text.Contains("+line 19 of medium.cs", StringComparison.Ordinal) && trimmed.Tokens <= 300, $"the rest kept whole, within the budget ({trimmed.Tokens} tokens)");
        var tiny = CommitMessages.Trim(Encoding.UTF8.GetBytes(diff), 20);
        Check.Equal(3, tiny.Summarised, "a budget too small for any file: every file summarised");

        Check.Equal("Add the parser\n\nIt reads bytes.", CommitMessages.Clean("```text\nAdd the parser\n\nIt reads bytes.  \n```"), "a fenced reply: its body");
        Check.Equal("Fix the tree", CommitMessages.Clean("Commit message: \"Fix the tree\""), "a lead-in and quotes taken off");
        var settings = new GitSettings { CommitStyle = "conventional" };
        Check.True(CommitMessages.Instructions(settings, true, true, 2).Contains("Conventional Commits", StringComparison.Ordinal)
            && CommitMessages.Instructions(settings, true, true, 2).Contains("Nothing is staged", StringComparison.Ordinal)
            && CommitMessages.Instructions(settings, true, true, 2).Contains("2 large files", StringComparison.Ordinal), "the style, every change and the summarised files said");
        Check.True(CommitMessages.Instructions(new GitSettings(), true, false, 0).Contains("recent subjects", StringComparison.Ordinal), "auto: the repository's own style");
        Check.True(CommitMessages.Instructions(new GitSettings { CommitPrompt = "Write in French." }, false, false, 0).StartsWith("Write in French.", StringComparison.Ordinal), "commitPrompt replaces the instructions");
    }

    [Test]
    public static void AFakeModelsMessageStreamsIntoTheBoxAsAnEditableDraftAndNothingCommitsByItself()
    {
        if (Repo.Missing() is { } why)
        {
            Console.WriteLine($"  skipped: {why}");
            return;
        }

        var provider = new FakeCommitProvider { Pieces = ["```\n", "Add the ", "parser", "\n\nIt reads", " bytes.\n```"] };
        CommitMessageProviders.Register(provider);
        try
        {
            using var repo = new Repo();
            var session = repo.Session;
            repo.Until(() => session.Repository?.Status.Changes.Count == 2, "the repository opens");
            repo.Act(SourceControlView.Stage, repo.File("a.txt"));
            repo.Until(() => session.State.Status.Staged.Count == 1, "a.txt staged");
            var box = session.Panel.Blocks.OfType<PanelMessageBox>().Single();
            Check.True(box.Actions[0] is { ActionId: SourceControlView.Generate, Enabled: true } button && button.Tooltip.Contains("fake-7b", StringComparison.Ordinal)
                && button.Menu.Any(m => m.Title.Contains("fake-7b", StringComparison.Ordinal)),
                $"the button's tooltip names the model it will use, its menu the loaded ones ({box.Actions[0].Tooltip})");
            var versions = new List<int>();
            session.Panel.Changed += (_, _) => versions.Add(session.State.MessageVersion);
            string head = repo.Git("rev-parse", "HEAD");
            repo.Act(SourceControlView.Generate, null);
            repo.Until(() => !session.State.Generating && session.State.MessageStatus?.StartsWith("Written by", StringComparison.Ordinal) == true, "the message is written");
            Check.Equal("Add the parser\n\nIt reads bytes.", session.Draft, "the draft, cleaned (no fence)");
            Check.Equal("Add the parser\n\nIt reads bytes.", session.State.Message, "in the box");
            Check.True(versions.Distinct().Count() >= 2, $"it streamed into the box as it came ({string.Join(",", versions.Distinct())})");
            var request = provider.Requests.Single();
            Check.True(request.Diff.Contains("a.txt", StringComparison.Ordinal) && !request.Diff.Contains("new file.txt", StringComparison.Ordinal), "from the staged diff only");
            Check.True(request.Examples.Contains("Initial commit") && request.Instructions.Contains("recent subjects", StringComparison.Ordinal), "with the repository's recent subjects as its style");
            Check.True(session.State.MessageStatus!.Contains("fake-7b", StringComparison.Ordinal) && session.State.MessageStatus.Contains("staged", StringComparison.Ordinal), $"the line under the box names the model ({session.State.MessageStatus})");
            Check.Equal(head, repo.Git("rev-parse", "HEAD"), "nothing was committed by itself");
            Check.True(session.State.Status.Staged.Count == 1, "the change is still staged");

            // The user edits the draft; nothing staged: every change is described, said so.
            repo.Act(SourceControlView.Message, "Add the parser (edited)");
            Check.Equal("Add the parser (edited)", session.Draft, "the draft is the user's to edit");
            repo.Act(SourceControlView.Unstage, repo.File("a.txt"));
            repo.Until(() => session.State.Status.Staged.Count == 0, "unstaged");
            provider.Pieces = ["Update a and add a file"];
            repo.Act(SourceControlView.Generate, null);
            repo.Until(() => !session.State.Generating && session.State.MessageStatus?.StartsWith("Written by", StringComparison.Ordinal) == true, "written again");
            Check.True(provider.Requests[1].Diff.Contains("new file.txt", StringComparison.Ordinal) && provider.Requests[1].Instructions.Contains("Nothing is staged", StringComparison.Ordinal)
                && session.State.MessageStatus!.Contains("every change", StringComparison.Ordinal), "nothing staged: every change, said so");
            Check.Equal(head, repo.Git("rev-parse", "HEAD"), "still nothing committed");
        }
        finally
        {
            CommitMessageProviders.Unregister(provider.Name);
        }
    }

    [Test]
    public static void StopKeepsWhatWasWrittenAndWithNoModelLoadedItSaysSoAndOffersTheLoad()
    {
        if (Repo.Missing() is { } why)
        {
            Console.WriteLine($"  skipped: {why}");
            return;
        }

        var provider = new FakeCommitProvider { Pieces = ["Add the", " parser"], Hold = new TaskCompletionSource() };
        CommitMessageProviders.Register(provider);
        int loads = 0;
        EditorCommands.Register(new EditorCommand("test.loadChat", "Test: load", (_, _) =>
        {
            loads++;
            return Task.CompletedTask;
        }));
        try
        {
            using var repo = new Repo();
            var session = repo.Session;
            repo.Until(() => session.Repository?.Status.Changes.Count == 2, "the repository opens");
            repo.Act(SourceControlView.Generate, null);
            repo.Until(() => session.State.Generating && session.Draft == "Add the", "the first piece is in the box");
            var box = session.Panel.Blocks.OfType<PanelMessageBox>().Single();
            Check.True(box is { Busy: true, Actions: [{ ActionId: SourceControlView.Stop }, ..] }, "while it writes: the box is busy and Stop takes the button's place");
            Check.True(repo.Activities.Any(t => t.Contains("fake-7b", StringComparison.Ordinal)), "the writing is an activity (with Cancel)");
            repo.Act(SourceControlView.Stop, null);
            repo.Until(() => !session.State.Generating, "Stop ends it");
            Check.Equal("Add the", session.Draft, "what was written is kept as the draft");
            Check.True(session.State.MessageStatus!.StartsWith("Stopped", StringComparison.Ordinal), "and says it stopped");

            provider.Model = null;                                // nothing loaded
            repo.Act(SourceControlView.Generate, null);
            repo.Pump();
            Check.True(session.State.MessageStatus!.Contains("No model is loaded", StringComparison.Ordinal) && session.State.MessageTone == PanelTone.Warning, "no model: it says so");
            Check.Equal(0, loads, "and loads nothing by itself (note 23)");
            Check.True(session.Panel.Blocks.OfType<PanelButtons>().Any(b => b.Buttons.Any(x => x is { Title: "Load the chat model", ActionId: SourceControlView.LoadModel })), "it offers to load the chat model");
            Check.True(session.Panel.Blocks.OfType<PanelMessageBox>().Single().Actions[0].Tooltip.Contains("No model is loaded", StringComparison.Ordinal), "the button's tooltip says why");
            repo.Act(SourceControlView.LoadModel, null);
            repo.Pump();
            Check.Equal(1, loads, "the offer runs the load command only when pressed");
            Check.Equal(1, provider.Requests.Count, "the model was asked only once");
        }
        finally
        {
            CommitMessageProviders.Unregister(provider.Name);
            EditorCommands.Unregister("test.loadChat");
        }
    }

    [Test]
    public static async Task TheAiProviderWritesWithALoadedRolesLeaseAndItsSettingsAndNeverLoads()
    {
        var provider = new ChatProvider();
        ModelProviders.Register(provider);
        try
        {
            var settings = new AiSettings { ModelProvider = ChatProvider.ProviderName, Model = "coder-7b", ChatTemperature = 0.15f, ChatMaxTokens = 77, CompletionModel = "" };
            using var models = new AiModels(() => settings);
            var ai = new AiCommitMessages(() => settings, models);
            var plan = ai.Plan("loaded");
            Check.True(plan is { Model: null, LoadCommand: AiPlugin.LoadModelCommand } && plan.Why!.Contains("No model is loaded", StringComparison.Ordinal), $"nothing loaded: it says so and names the load ({plan.Why})");
            Check.True(ai.Models() is [{ Value: "chat", Loaded: false }, { Value: "agent", Loaded: false }], "the menu lists the chat's model and the agent's (the chat's by default), not loaded");
            Check.Equal(0, provider.Acquired, "asking loaded nothing");
            bool refused = false;
            try
            {
                await foreach (var _ in ai.WriteAsync(new CommitMessageRequest("i", "d"), CancellationToken.None))
                {
                }
            }
            catch (InvalidOperationException)
            {
                refused = true;
            }

            Check.True(refused, "writing with nothing loaded is refused");
            Check.Equal(0, provider.Acquired, "and writing with nothing loaded loads nothing");

            _ = await models.ChatAsync(CancellationToken.None);   // the user loads the chat model
            plan = ai.Plan("loaded");
            Check.True(plan is { Model: "coder-7b", Role: "chat", Why: null }, $"loaded: the chat's model ({plan.Model}, {plan.Role})");
            Check.True(ai.Plan("agent") is { Model: "coder-7b", Role: "agent" }, "the agent shares the chat's copy, so it is loaded too");
            Check.True(ai.Plan("completion").Model is null, "completion has no model of its own loaded");
            Check.True(ai.Plan("coder-7b").Model == "coder-7b" && ai.Plan("other-model").Model is null, "a model's name: the role that has it loaded");
            var text = new StringBuilder();
            await foreach (var piece in ai.WriteAsync(new CommitMessageRequest("Write a message.", "diff --git a/x b/x\n+y\n") { Examples = ["Add x"] }, CancellationToken.None))
            {
                text.Append(piece);
            }

            Check.Equal("Add y", text.ToString(), "the loaded model's reply, streamed");
            Check.Equal(1, provider.Acquired, "one load in all: the user's");
            var request = provider.Model.Requests.Single();
            Check.True(request.Options is { Temperature: 0.15f, NumPredict: 77 }, $"the chat role's own temperature and reply limit ({request.Options?.Temperature}, {request.Options?.NumPredict})");
            Check.True(request.Messages[0] is { Role: "system", Content: "Write a message." } && request.Messages[1].Content.Contains("- Add x", StringComparison.Ordinal) && request.Messages[1].Content.Contains("+y", StringComparison.Ordinal),
                "the instructions as the system's words, the examples and the diff as the user's");
        }
        finally
        {
            ModelProviders.Unregister(ChatProvider.ProviderName);
        }
    }

    // ---- the plug-in ----

    [Test]
    public static void TheGitPluginLoadsFromItsFolderAndCanBeRemoved()
    {
        Check.True(SourceControlProviders.Find(GitProvider.ProviderName) is null, "before it loads, \"git\" is not registered");
        Check.Throws<NotRegisteredException>(() => SourceControlProviders.Get(GitProvider.ProviderName), "SourceControlProviders.Get(\"git\") before it loads");
        using var temp = new TempFolder();
        var output = PhasePTests.PluginOutput(GitPackage.PackageName);
        var folder = Path.Combine(temp.Path, "plugins", GitPackage.PackageName);
        foreach (var file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(folder, Path.GetRelativePath(output, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        Check.True(!File.Exists(Path.Combine(folder, "Craft.Abstractions.dll")) && !File.Exists(Path.Combine(folder, "Idrak.Abstraction.dll")), "it carries no copy of the shared contracts");
        var result = CraftPlugins.LoadFolder(folder);
        try
        {
            Check.True(result is { Loaded: true, Manifest.ContractVersion: "1.1" }, $"it loads from its folder ({result.Error})");
            var provider = SourceControlProviders.Get(GitProvider.ProviderName);
            Check.True(AssemblyLoadContext.GetLoadContext(provider.GetType().Assembly) != AssemblyLoadContext.Default, "\"git\" comes from the plug-in's own load context");
            Check.True(EditorPlugins.Find(GitSettings.PluginName) is not null && EditorRailItems.Find(SourceControlView.PanelId) is { Title: "Source Control" }, "the editor plug-in and its rail item");
            Check.True(EditorCommands.Find(SourceControlProviders.BranchCommand) is not null && EditorCommands.Find(GitPlugin.GenerateCommand) is { Title: "Source Control: Generate commit message" },
                "the status bar's branch command and the palette's Generate commit message");
            Check.True(EditorMenus.ItemsOf(EditorContextMenus.Tree).Any(i => i.CommandId == GitPlugin.StageFileCommand) && EditorMenus.ItemsOf(EditorContextMenus.Tab).Any(i => i.CommandId == GitPlugin.OpenChangesCommand),
                "the tree's and the tab's git items");
            Check.True(EditorPlugins.Find(GitSettings.PluginName)!.Settings.All(s => s.Shape?.Section == SettingSections.SourceControl), "its settings in the Source Control section");
        }
        finally
        {
            var loaded = AssemblyLoadContext.All.Where(c => c != AssemblyLoadContext.Default).SelectMany(c => c.Assemblies)
                .LastOrDefault(x => x.GetName().Name == GitPackage.PackageName && x.Location.StartsWith(folder, StringComparison.Ordinal));
            if (loaded is not null && IdrakPlugins.PackageOf(loaded) is { } package)
            {
                IdrakPlugins.UnregisterAll(package);
            }
        }

        Check.True(SourceControlProviders.Find(GitProvider.ProviderName) is null && EditorRailItems.Find(SourceControlView.PanelId) is null && EditorCommands.Find(SourceControlProviders.BranchCommand) is null,
            "removed: its provider, rail item and commands are gone");
        Check.True(!CraftSettings.Allows(SettingLayer.Workspace, "CRAFT_PLUGIN_GIT_PATH") && CraftSettings.Allows(SettingLayer.User, "CRAFT_PLUGIN_GIT_PATH"),
            "the git program is the machine's setting: a workspace's file cannot name it");
    }

    // ---- helpers ----

    /// <summary>A temporary git repository (main: a.txt and b.txt committed; then a.txt changed and "new file.txt" untracked) with the git plug-in active in a host whose UI thread the test pumps.</summary>
    internal sealed class Repo : IDisposable
    {
        private static readonly string? GitPath = GitProgram.Find("", out _)?.Path;
        private readonly TempFolder _temp = new();
        private readonly ConcurrentQueue<Action> _queue = new();
        private readonly GitPackage _package = new();
        private readonly SynchronizationContext? _before = SynchronizationContext.Current;

        public Repo()
        {
            Root = Path.GetFullPath(Path.Combine(_temp.Path, "repo"));
            Directory.CreateDirectory(Root);
            Git("init", "-q", "-b", "main");
            Git("config", "user.email", "test@example.com");
            Git("config", "user.name", "Test");
            Git("config", "core.autocrlf", "false");
            System.IO.File.WriteAllText(this.File("a.txt"), "one\ntwo\nthree\n");
            System.IO.File.WriteAllText(this.File("b.txt"), "b\n");
            Git("add", "-A");
            Git("commit", "-q", "-m", "Initial commit");
            System.IO.File.WriteAllText(this.File("a.txt"), "one\ntwo changed\nthree\n");
            System.IO.File.WriteAllText(this.File("new file.txt"), "new\n");

            // The test thread is the UI thread: the session's awaits come back through the queue the test pumps.
            SynchronizationContext.SetSynchronizationContext(new QueueContext(_queue));
            var vars = new Dictionary<string, string> { ["CRAFT_PLUGIN_GIT_REFRESHMS"] = "20", ["CRAFT_PLUGIN_GIT_GUTTERDELAYMS"] = "0" };
            IdrakPlugins.RegisterAll(_package);
            Host = new EditorHost(new CraftOptions { DataFolder = Path.Combine(_temp.Path, "data") }, _queue.Enqueue, Presenter, name => vars.TryGetValue(name, out var v) ? v : null)
            {
                WorkspaceFolder = Root,
                UndoPresenter = (text, undo, _) =>
                {
                    Undo = (text, undo);
                    return true;
                },
            };
            Host.ActivityCenter.Changed += (_, _) =>
            {
                foreach (var running in Host.Activities.Running)
                {
                    Activities.Add(running.Title);
                }
            };
            Host.RefreshPlugins();
            Session = GitPlugin.Session ?? throw new InvalidOperationException("the git plug-in did not activate");
            Pump();
        }

        public string Root { get; }

        public EditorHost Host { get; }

        public GitSession Session { get; }

        public AnsweringPresenter Presenter { get; } = new();

        public (string Text, Action Run)? Undo { get; private set; }

        public ConcurrentBag<string> Activities { get; } = [];

        /// <summary>Why the real repository tests cannot run here, or null.</summary>
        public static string? Missing() => GitPath is null ? "git is not on this machine's PATH" : null;

        public string File(string name) => Path.Combine(Root, name);

        /// <summary>Runs git in the repository (the test's own setup and checks), its output trimmed.</summary>
        public string Git(params string[] arguments)
        {
            var start = new ProcessStartInfo(GitPath!) { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Trim();
        }

        /// <summary>A block's action, as the view runs it on the UI thread.</summary>
        public void Act(string action, string? value)
        {
            _ = Session.ActAsync(action, value);
            Pump();
        }

        public void Pump()
        {
            while (_queue.TryDequeue(out var action))
            {
                action();
            }
        }

        /// <summary>Pumps the UI thread until <paramref name="done"/> holds, at most twenty seconds.</summary>
        public void Until(Func<bool> done, string what)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                Pump();
                if (done())
                {
                    return;
                }

                Check.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"{what} (waited 20 s)");
                Thread.Sleep(5);
            }
        }

        public T Wait<T>(Task<T> task)
        {
            Until(() => task.IsCompleted, "the task ends");
            return task.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Pump();
            Host.Dispose();
            IdrakPlugins.UnregisterAll(_package);
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

    /// <summary>Answers every question with its first choice and accepts every proposal, recording both.</summary>
    internal sealed class AnsweringPresenter : IEditorPresenter
    {
        public ConcurrentBag<string> Questions { get; } = [];

        public ConcurrentBag<string> Proposals { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken)
        {
            Proposals.Add(proposal.Title);
            return Task.FromResult(true);
        }

        public ConcurrentBag<(string Title, IReadOnlyList<DiffNote> Notes)> Diffs { get; } = [];

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<bool> ShowDiffAsync(string title, string path, TextDiff diff, CancellationToken cancellationToken)
        {
            Diffs.Add((title, []));
            return Task.FromResult(true);
        }

        public Task<bool> ShowDiffAsync(string title, string path, TextDiff diff, IReadOnlyList<DiffNote> notes, CancellationToken cancellationToken)
        {
            Diffs.Add((title, notes));
            return Task.FromResult(true);
        }

        public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken)
        {
            Questions.Add(question.Message);
            return Task.FromResult(0);
        }
    }

    /// <summary>A commit message provider that streams its pieces (holding after the first while <see cref="Hold"/> is set).</summary>
    private sealed class FakeCommitProvider : ICommitMessageProvider
    {
        public string Name => "fake-commit";

        public string Summary => "the source control tests' writer";

        public string? Model { get; set; } = "fake-7b";

        public IReadOnlyList<string> Pieces { get; set; } = [];

        public TaskCompletionSource? Hold { get; init; }

        public List<CommitMessageRequest> Requests { get; } = [];

        public IReadOnlyList<CommitModelChoice> Models() => Model is null ? [] : [new CommitModelChoice("chat", "Chat · " + Model, true)];

        public CommitModelPlan Plan(string choice) => Model is null
            ? new CommitModelPlan(null, null, "No model is loaded: load the chat model.") { LoadCommand = "test.loadChat" }
            : new CommitModelPlan(Model, "chat", null);

        public async IAsyncEnumerable<string> WriteAsync(CommitMessageRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            for (int i = 0; i < Pieces.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return Pieces[i];
                await Task.Delay(10, cancellationToken).ConfigureAwait(false);
                if (i == 0 && Hold is { } hold)
                {
                    await hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>A model provider whose model is a scripted chat model replying "Add y".</summary>
    private sealed class ChatProvider : IModelProvider
    {
        public const string ProviderName = "commit-message-test";

        public int Acquired { get; private set; }

        public FakeChatModel Model { get; } = FakeChatModel.Script(FakeChatModel.Answer("Add y"));

        public string Name => ProviderName;

        public string Summary => "the commit message tests' provider";

        public Task<ModelLease> AcquireAsync(ModelRequest request, IProgress<ModelLoadStatus>? progress = null, CancellationToken cancellationToken = default)
        {
            Acquired++;
            return Task.FromResult(new ModelLease(request.Model, Model, null, 4096, TimeSpan.FromMilliseconds(1)));
        }
    }

    /// <summary>A repository whose status the test sets.</summary>
    private sealed class FakeRepository(string root) : ISourceControlRepository
    {
        public string Provider => "fake";

        public string Root { get; } = root;

        public SourceControlStatus Status { get; set; } = SourceControlStatus.None;

        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StageAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UnstageAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task CommitAsync(string message, bool amend, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> ReadStagedAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> ReadCommittedAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class NoDialogs : IFileDialogs
    {
        public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class SilentUi : IShellUi
    {
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(UnsavedChoice.Cancel);

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => Task.FromResult(-1);
    }
}
