// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.Git;

/// <summary>What the Source Control view shows at one moment (the session builds it from what it read; the view makes the blocks).</summary>
public sealed record SourceControlViewState
{
    /// <summary>Why git cannot run here (with what to do), or null.</summary>
    public string? Unavailable { get; init; }

    /// <summary>Whether no folder is open.</summary>
    public bool NoFolder { get; init; }

    /// <summary>The open folder when it is in no repository, or null.</summary>
    public string? NotRepository { get; init; }

    /// <summary>Whether the folder's repository is being opened.</summary>
    public bool Opening { get; init; }

    /// <summary>The repository's status.</summary>
    public SourceControlStatus Status { get; init; } = SourceControlStatus.None;

    /// <summary>The local branches.</summary>
    public IReadOnlyList<GitBranchInfo> Branches { get; init; } = [];

    /// <summary>The commit message box's text when its version is new.</summary>
    public string Message { get; init; } = "";

    /// <summary>The box's version (raised whenever the session sets its text: a streamed draft, a commit that clears it).</summary>
    public int MessageVersion { get; init; }

    /// <summary>Whether a message is being written into the box.</summary>
    public bool Generating { get; init; }

    /// <summary>The line under the box (who wrote the draft, why none could), or null.</summary>
    public string? MessageStatus { get; init; }

    /// <summary>The line's colour role.</summary>
    public PanelTone MessageTone { get; init; }

    /// <summary>The command that loads the chat's model, when the user is offered to load it (nothing loaded), or null.</summary>
    public string? LoadCommand { get; init; }

    /// <summary>Whether the next commit amends the last one.</summary>
    public bool Amend { get; init; }

    /// <summary>The loaded models the Generate button's menu lists.</summary>
    public IReadOnlyList<CommitModelChoice> Models { get; init; } = [];

    /// <summary>What the Generate button's tooltip says (the model it will use, or why none).</summary>
    public string GenerateTooltip { get; init; } = "Generate commit message";

    /// <summary>Whether a commit message provider is registered (the AI plug-in).</summary>
    public bool CanGenerate { get; init; } = true;

    /// <summary>The user's action running now ("Pushing…"), or null.</summary>
    public string? Working { get; init; }

    /// <summary>What the last action that failed said, or null.</summary>
    public string? Error { get; init; }

    /// <summary>The last review's findings (X part 11 item 4), in the order they came.</summary>
    public IReadOnlyList<ReviewItem> Findings { get; init; } = [];

    /// <summary>Whether a review runs now.</summary>
    public bool Reviewing { get; init; }

    /// <summary>Whether a review ran (its findings, or "no findings", are shown until cleared).</summary>
    public bool Reviewed { get; init; }

    /// <summary>The line under the findings (the model, how the changes were chosen, why none could review), or null.</summary>
    public string? ReviewStatus { get; init; }

    /// <summary>The review line's colour role.</summary>
    public PanelTone ReviewTone { get; init; }

    /// <summary>The command that loads the review's model, when the user is offered to load it, or null.</summary>
    public string? ReviewLoadCommand { get; init; }

    /// <summary>What the Review button's tooltip says (the model it will use, or why none).</summary>
    public string ReviewTooltip { get; init; } = "Review changes";

    /// <summary>Whether a code review provider is registered (the AI plug-in).</summary>
    public bool CanReview { get; init; } = true;
}

/// <summary>
/// The Source Control view as blocks (X part 8, on B11's block panels): the commit message box with Generate (its model
/// menu) or Stop, Commit and Amend, Pull, Push, Sync, Fetch, Stash and Pop, the branch with ahead and behind and a new
/// branch, then the merge conflicts, the staged changes and the changes, each a group of files with their icons, state
/// letters and colours and their actions on hover. Each part is a top-level block, so a change redraws only its part (the
/// box keeps what is typed). Headless: the tests read the blocks.
/// </summary>
public static class SourceControlView
{
    /// <summary>The panel's id (its rail item's too).</summary>
    public const string PanelId = "scm.view";

    /// <summary>A sparkle (the design's Agent icon): Generate commit message.</summary>
    public const string SparkleIcon = RailIcons.Agent;

    /// <summary>A checked box: review the changes (X part 11).</summary>
    public const string ReviewIcon = "M9 11l3 3L22 4M21 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11";

    /// <summary>A plus: stage.</summary>
    public const string StageIcon = "M12 5v14M5 12h14";

    /// <summary>A minus: unstage.</summary>
    public const string UnstageIcon = "M5 12h14";

    /// <summary>An arrow turning back: discard.</summary>
    public const string DiscardIcon = "M9 14L4 9l5-5M4 9h11a5 5 0 0 1 0 10h-3";

    /// <summary>A page with an arrow out: open the file.</summary>
    public const string OpenIcon = "M14 3h7v7M10 14L21 3M19 14v6H4V5h6";

    /// <summary>A square: stop.</summary>
    public const string StopIcon = "M7 7h10v10H7z";

    /// <summary>A check: mark resolved (stage).</summary>
    public const string CheckIcon = "M5 12.5l4.5 4.5L19 7.5";

    /// <summary>An arrow up: accept the current change.</summary>
    public const string CurrentIcon = "M12 19V5M6 11l6-6 6 6";

    /// <summary>An arrow down: accept the incoming change.</summary>
    public const string IncomingIcon = "M12 5v14M6 13l6 6 6-6";

    /// <summary>Two bars: accept both.</summary>
    public const string BothIcon = "M8 5v14M16 5v14";

    // The actions the blocks run (IEditorViewPanel.ActAsync's ids).
#pragma warning disable CS1591 // The action ids, named as they act.
    public const string Open = "open";
    public const string OpenFile = "openFile";
    public const string Stage = "stage";
    public const string Unstage = "unstage";
    public const string Discard = "discard";
    public const string StageAll = "stageAll";
    public const string UnstageAll = "unstageAll";
    public const string DiscardAll = "discardAll";
    public const string Message = "message";
    public const string Commit = "commit";
    public const string Amend = "amend";
    public const string Generate = "generate";
    public const string Stop = "stop";
    public const string PickModel = "pickModel";
    public const string LoadModel = "loadModel";
    public const string Pull = "pull";
    public const string Push = "push";
    public const string Fetch = "fetch";
    public const string Sync = "sync";
    public const string Stash = "stash";
    public const string Pop = "pop";
    public const string Branch = "branch";
    public const string NewBranch = "newBranch";
    public const string AcceptCurrent = "acceptCurrent";
    public const string AcceptIncoming = "acceptIncoming";
    public const string AcceptBoth = "acceptBoth";
    public const string Resolved = "resolved";
    public const string Retry = "retry";
    public const string Settings = "settings";
    public const string Init = "init";
    public const string OpenFolder = "openFolder";
    public const string Dismiss = "dismiss";
    public const string Review = "review";
    public const string StopReview = "stopReview";
    public const string LoadReviewModel = "loadReviewModel";
    public const string OpenFinding = "openFinding";
    public const string ApplyFix = "applyFix";
    public const string DismissFinding = "dismissFinding";
    public const string CopyFindings = "copyFindings";
    public const string ClearFindings = "clearFindings";
#pragma warning restore CS1591

    /// <summary>The blocks of <paramref name="state"/>.</summary>
    public static IReadOnlyList<PanelBlock> Blocks(SourceControlViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Unavailable is { } why)
        {
            return
            [
                new PanelCard("Git is not available", [
                    new PanelText(why, PanelTextStyle.Secondary),
                    new PanelButtons([new PanelButton("Try again", Retry, PanelButtonKind.Primary), new PanelButton("Open its settings", Settings)]),
                ]) { Tone = PanelTone.Warning, Icon = RailIcons.SourceControl, Variable = "CRAFT_PLUGIN_GIT_PATH" },
            ];
        }

        if (state.NoFolder)
        {
            return
            [
                new PanelCard("No folder is open", [
                    new PanelText("Open a folder to see its changes, commit them, and switch branches.", PanelTextStyle.Secondary),
                    new PanelButtons([new PanelButton("Open folder", OpenFolder, PanelButtonKind.Primary)]),
                ]) { Icon = RailIcons.SourceControl },
            ];
        }

        if (state.NotRepository is { } folder)
        {
            return
            [
                new PanelCard("Not a git repository", [
                    new PanelText($"{folder} is not in a git repository. Make it one to track its changes.", PanelTextStyle.Secondary),
                    new PanelButtons([new PanelButton("Initialize repository", Init, PanelButtonKind.Primary)]),
                ]) { Icon = RailIcons.SourceControl },
            ];
        }

        if (state.Opening)
        {
            return [new PanelMeter("Reading the repository", -1)];
        }

        var status = state.Status;
        var blocks = new List<PanelBlock>();
        if (state.Error is { } error)
        {
            blocks.Add(new PanelCard("Git said", [
                new PanelText(error, PanelTextStyle.Secondary) { Tone = PanelTone.Error },
                new PanelButtons([new PanelButton("Dismiss", Dismiss, PanelButtonKind.Flat)]),
            ]) { Tone = PanelTone.Error });
        }

        blocks.Add(MessageBox(state));
        if (state.LoadCommand is not null)
        {
            blocks.Add(new PanelButtons([new PanelButton("Load the chat model", LoadModel) { Tooltip = "Loads the chat's model (its progress and Cancel in the status bar); a message is written once it is loaded" }]));
        }

        int stagedCount = status.Staged.Count;
        string commit = state.Amend ? "Commit (amend)" : stagedCount > 0 ? $"Commit {Files(stagedCount)}" : "Commit";
        blocks.Add(new PanelButtons([
            new PanelButton(commit, Commit, PanelButtonKind.Primary)
            {
                Enabled = !state.Generating && state.Working is null && (status.Staged.Count + status.Changes.Count > 0 || state.Amend),
                Tooltip = stagedCount > 0 ? "Commit the staged changes (Ctrl+Enter in the message)" : "Nothing is staged: commit asks to stage every change first (Ctrl+Enter in the message)",
            },
        ]));
        blocks.Add(new PanelSwitch(Amend, "Amend the last commit", state.Amend) { Detail = state.Amend ? "The next commit replaces the last one, with this message." : null });
        AddReview(blocks, state);
        blocks.Add(SyncButtons(state));
        if (state.Working is { } working)
        {
            blocks.Add(new PanelMeter(working, -1));
        }

        blocks.Add(BranchPicker(state));
        blocks.Add(new PanelInput(NewBranch, "New branch", "") { Placeholder = "feature/name", Submit = "Create", Code = true, Tooltip = "Creates the branch from here and switches to it" });
        if (status.Conflicts.Count > 0)
        {
            blocks.Add(new PanelFileList("conflicts", "Merge conflicts", [.. status.Conflicts.Select(ConflictRow)]));
        }

        blocks.Add(new PanelFileList("staged", "Staged changes", [.. status.Staged.Select(StagedRow)])
        {
            Actions = status.Staged.Count > 0 ? [new PanelIconButton(UnstageIcon, UnstageAll, "Unstage all")] : [],
            Empty = "Stage a change with + to commit it.",
        });
        blocks.Add(new PanelFileList("changes", "Changes", [.. status.Changes.Select(ChangeRow)])
        {
            Actions = status.Changes.Count > 0
                ? [new PanelIconButton(DiscardIcon, DiscardAll, "Discard all changes (Undo for a while)") { Tone = PanelTone.Error }, new PanelIconButton(StageIcon, StageAll, "Stage all changes")]
                : [],
            Empty = "No changes in the working copy.",
        });
        return blocks;
    }

    /// <summary>The rail's badge for <paramref name="state"/>: its changed files' count, or null when there are none.</summary>
    public static string? Badge(SourceControlViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        int count = state.Unavailable is null && state.NotRepository is null && !state.NoFolder ? state.Status.Count : 0;
        return count == 0 ? null : count > 999 ? "999+" : count.ToString(CultureInfo.InvariantCulture);
    }

    private static PanelMessageBox MessageBox(SourceControlViewState state)
    {
        var branch = state.Status.Branch is { } name ? $" on {name}" : "";
        PanelIconButton action = state.Generating
            ? new PanelIconButton(StopIcon, Stop, "Stop writing the message") { Tone = PanelTone.Error }
            : new PanelIconButton(SparkleIcon, Generate, state.GenerateTooltip)
            {
                Enabled = state.CanGenerate && state.Working is null,
                Tone = PanelTone.Accent,
                Menu = [.. state.Models.Where(m => m.Loaded).Select(m => new PanelOption(m.Value, m.Title))],
                MenuActionId = PickModel,
            };
        PanelIconButton review = state.Reviewing
            ? new PanelIconButton(StopIcon, StopReview, "Stop the review (its findings so far stay)") { Tone = PanelTone.Error }
            : new PanelIconButton(ReviewIcon, Review, state.ReviewTooltip)
            {
                Enabled = state.CanReview && state.Working is null && state.Status.Count > 0,
                Tone = PanelTone.Accent,
            };
        return new PanelMessageBox(Message, state.Message)
        {
            Placeholder = $"Message (Ctrl+Enter to commit{branch})",
            Version = state.MessageVersion,
            SubmitActionId = Commit,
            Actions = [action, review],
            Busy = state.Generating,
            Status = state.MessageStatus,
            StatusTone = state.MessageTone,
            Variable = state.MessageStatus is null ? null : "CRAFT_PLUGIN_GIT_COMMITMODEL",
        };
    }

    // X part 11 item 4: the review's state, its findings by file (each with Apply fix and Dismiss), Copy all and Clear.
    private static void AddReview(List<PanelBlock> blocks, SourceControlViewState state)
    {
        if (state.Reviewing)
        {
            blocks.Add(new PanelMeter(state.ReviewStatus ?? "Reviewing the changes…", -1) { Detail = state.Findings.Count > 0 ? Findings(state.Findings.Count) + " so far" : null });
        }
        else if (state.ReviewStatus is { } status)
        {
            blocks.Add(new PanelText(status, PanelTextStyle.Caption) { Tone = state.ReviewTone, Variable = "CRAFT_PLUGIN_AI_REVIEWMODEL" });
        }

        if (state.ReviewLoadCommand is not null)
        {
            blocks.Add(new PanelButtons([new PanelButton("Load the model", LoadReviewModel) { Tooltip = "Loads the review's model (its progress and Cancel in the status bar); review once it is loaded" }]));
        }

        if (state.Findings.Count == 0)
        {
            return;
        }

        var groups = new List<PanelResultGroup>();
        foreach (var file in state.Findings.GroupBy(f => f.Finding.Path, StringComparer.Ordinal))
        {
            var rows = new List<PanelResultRow>();
            foreach (var item in file.OrderBy(i => i.Finding.Line))
            {
                var finding = item.Finding;
                rows.Add(new PanelResultRow("finding:" + item.Id, finding.Message)
                {
                    Prefix = finding.Line > 0 ? "L" + finding.Line.ToString(CultureInfo.InvariantCulture) : null,
                    Detail = finding.Reason.Length > 0 ? finding.Reason : null,
                    Badge = ReviewFinding.SeverityWord(finding.Severity),
                    Tone = finding.Severity switch
                    {
                        DiagnosticSeverity.Error => PanelTone.Error,
                        DiagnosticSeverity.Warning => PanelTone.Warning,
                        _ => PanelTone.Accent,
                    },
                    ActionId = OpenFinding,
                    Value = item.Id,
                    Buttons =
                    [
                        new PanelButton("Apply fix", ApplyFix, PanelButtonKind.Flat) { Value = item.Id, Tooltip = "The model's fix of this finding, as a diff you accept" },
                        new PanelButton("Dismiss", DismissFinding, PanelButtonKind.Flat) { Value = item.Id, Tooltip = "Drop this finding" },
                    ],
                });
            }

            var icon = FileIcons.For(file.Key);
            groups.Add(new PanelResultGroup("review:" + file.Key, Path.GetFileName(file.Key), rows)
            {
                Detail = Path.GetDirectoryName(file.Key)?.Replace('\\', '/') is { Length: > 0 } folder ? folder : null,
                Badge = rows.Count.ToString(CultureInfo.InvariantCulture),
                Glyph = icon.Badge,
                GlyphArgb = icon.Argb,
                Tooltip = file.Key,
            });
        }

        blocks.Add(new PanelText($"Review · {Findings(state.Findings.Count)}", PanelTextStyle.Heading));
        blocks.Add(new PanelResults("review", groups) { Version = state.Findings.Count });
        blocks.Add(new PanelButtons([
            new PanelButton("Copy all", CopyFindings, PanelButtonKind.Flat) { Tooltip = "Copies every finding, one a line (path:line: severity: message — reason)" },
            new PanelButton("Clear", ClearFindings, PanelButtonKind.Flat) { Enabled = !state.Reviewing, Tooltip = "Drops the findings and their notes in the editor" },
        ]));
    }

    private static string Findings(int count) => count == 1 ? "1 finding" : string.Create(CultureInfo.InvariantCulture, $"{count} findings");

    private static PanelButtons SyncButtons(SourceControlViewState state)
    {
        var status = state.Status;
        bool idle = state.Working is null;
        bool upstream = status.Upstream is not null;
        return new PanelButtons([
            new PanelButton(status.Behind > 0 ? $"Pull ↓{status.Behind}" : "Pull", Pull, PanelButtonKind.Flat) { Enabled = idle && upstream, Tooltip = upstream ? $"Pull from {status.Upstream}" : "The branch has no upstream" },
            new PanelButton(status.Ahead > 0 ? $"Push ↑{status.Ahead}" : "Push", Push, PanelButtonKind.Flat) { Enabled = idle, Tooltip = upstream ? $"Push to {status.Upstream}" : "Publish the branch to the remote" },
            new PanelButton("Sync", Sync, PanelButtonKind.Flat) { Enabled = idle && upstream, Tooltip = "Pull, then push" },
            new PanelButton("Fetch", Fetch, PanelButtonKind.Flat) { Enabled = idle, Tooltip = "Fetch from every remote (CRAFT_PLUGIN_GIT_AUTOFETCHMINUTES fetches in the background)" },
            new PanelButton("Stash", Stash, PanelButtonKind.Flat) { Enabled = idle && status.Changes.Count + status.Staged.Count > 0, Tooltip = "Put every change aside (untracked files too)" },
            new PanelButton(status.Stashes > 0 ? $"Pop ({status.Stashes})" : "Pop", Pop, PanelButtonKind.Flat) { Enabled = idle && status.Stashes > 0, Tooltip = "Take the last stash back" },
        ]);
    }

    private static PanelPicker BranchPicker(SourceControlViewState state)
    {
        var status = state.Status;
        var options = new List<PanelOption>();
        foreach (var branch in state.Branches)
        {
            options.Add(new PanelOption(branch.Name, branch.Name)
            {
                Detail = branch.Upstream is { } up
                    ? string.Create(CultureInfo.InvariantCulture, $"Tracks {up}{(branch.Ahead > 0 ? $" · ↑{branch.Ahead} to push" : "")}{(branch.Behind > 0 ? $" · ↓{branch.Behind} to pull" : "")}")
                    : "No upstream",
            });
        }

        string current = status.Branch ?? "";
        if (current.Length > 0 && !options.Exists(o => o.Value == current))
        {
            options.Insert(0, new PanelOption(current, status.Detached ? $"{current} (detached)" : current));
        }

        return new PanelPicker(Branch, "Branch", options, current)
        {
            Enabled = state.Working is null,
            Tooltip = status.Upstream is { } upstream
                ? string.Create(CultureInfo.InvariantCulture, $"{current} tracks {upstream}: ↑{status.Ahead} to push, ↓{status.Behind} to pull")
                : "Switch to another branch",
        };
    }

    private static PanelFileRow ChangeRow(SourceControlFile file) => Row(file, Open,
        [
            new PanelIconButton(OpenIcon, OpenFile, "Open the file"),
            new PanelIconButton(DiscardIcon, Discard, "Discard the change (Undo for a while)") { Tone = PanelTone.Error },
            new PanelIconButton(StageIcon, Stage, "Stage the change"),
        ]);

    private static PanelFileRow StagedRow(SourceControlFile file) => Row(file, Open,
        [
            new PanelIconButton(OpenIcon, OpenFile, "Open the file"),
            new PanelIconButton(UnstageIcon, Unstage, "Unstage the change"),
        ]);

    private static PanelFileRow ConflictRow(SourceControlFile file) => Row(file, OpenFile,
        [
            new PanelIconButton(CurrentIcon, AcceptCurrent, "Accept current (yours) in every conflict of the file"),
            new PanelIconButton(IncomingIcon, AcceptIncoming, "Accept incoming (theirs) in every conflict of the file"),
            new PanelIconButton(BothIcon, AcceptBoth, "Accept both in every conflict of the file"),
            new PanelIconButton(CheckIcon, Resolved, "Mark resolved (stage the file)"),
        ]);

    private static PanelFileRow Row(SourceControlFile file, string click, IReadOnlyList<PanelIconButton> actions)
    {
        string relative = file.RelativePath.Length > 0 ? file.RelativePath : Path.GetFileName(file.Path);
        int slash = relative.LastIndexOf('/');
        string? folder = slash > 0 ? relative[..slash] : null;
        string words = SourceControlStates.Words(file.State);
        return new PanelFileRow(file.Path, Path.GetFileName(file.Path), file.Letter)
        {
            Folder = folder,
            Tone = SourceControlStates.Tone(file.State),
            Struck = file.State == SourceControlState.Deleted,
            ActionId = click,
            Actions = actions,
            Tooltip = file.OldPath is { } old ? $"{relative} · {words} from {Path.GetFileName(old)}" : $"{relative} · {words}",
        };
    }

    private static string Files(int count) => count == 1 ? "1 file" : string.Create(CultureInfo.InvariantCulture, $"{count} files");
}
