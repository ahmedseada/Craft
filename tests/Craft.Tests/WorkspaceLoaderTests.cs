// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Settings;
using Craft.Editor;
using Craft.Editor.Activities;
using Craft.Editor.Shell;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// B10: a language's workspace in the notification centre (ready once per load with its facts, a failure in plain words
/// with Try again), the status bar's language state only while it loads or failed, and the language item's tooltip.
/// </summary>
[TestGroup("Notifications")]
public static class WorkspaceNoticeTests
{
    private static readonly CraftOptions Quick = new() { ActivityMinimumMilliseconds = 0, ActivityFrameMilliseconds = 1, NotificationsKeep = 20 };

    private static SemanticTelemetry Built(int generation, int projects = 1) =>
        new(Path.Combine("projects", "Craft"), "csproj", projects, 12, 30, 4L << 20, 2L << 20, TimeSpan.FromMilliseconds(2300), TimeSpan.Zero, generation) { Engine = "Roslyn" };

    [Test]
    public static void ReadyIsPostedOncePerLoadWithItsFacts()
    {
        using var host = new EditorHost(Quick);
        var toasts = new List<NotificationEntry>();
        host.Notifications.Toast += (_, e) => toasts.Add(e);
        var notices = new WorkspaceNotices(host.Notifications.Post);
        notices.Observe("C#", SemanticTelemetry.None with { WorkspaceRoot = Path.Combine("projects", "Craft") }, () => { });
        Check.Equal(0, notices.Posted, "nothing while the workspace loads");
        notices.Observe("C#", Built(1), () => { });
        notices.Observe("C#", Built(1), () => { });
        var entry = host.Notifications.Entries.Single(e => e.Source == "C#");
        Check.True(notices.Posted == 1 && entry is { Title: "C# workspace ready", Severity: NotificationSeverity.Success } && entry.Text == "1 project, 6 MB, 2.3 s (Craft)",
            $"one notification for the load: \"{entry.Title}: {entry.Text}\"");
        Check.True(toasts.Count == 0, "ready is never a toast");
        notices.Observe("C#", Built(2, projects: 3), () => { });
        var entries = host.Notifications.Entries.Where(e => e.Source == "C#").ToList();
        Check.True(notices.Posted == 2 && entries.Count == 1 && entries[0].Text.StartsWith("3 projects", StringComparison.Ordinal), "a new load of the same workspace takes the old one's place");
        notices.Observe("C#", Built(1) with { WorkspaceRoot = Path.Combine("projects", "Other") }, () => { });
        Check.Equal(2, host.Notifications.Entries.Count(e => e.Source == "C#"), "another workspace has its own");
    }

    [Test]
    public static async Task AFailureIsPlainWordsWithItsFix()
    {
        using var host = new EditorHost(Quick);
        var toasts = new List<NotificationEntry>();
        host.Notifications.Toast += (_, e) => toasts.Add(e);
        var notices = new WorkspaceNotices(host.Notifications.Post);
        int retried = 0;
        var failed = SemanticTelemetry.None with { WorkspaceRoot = Path.Combine("projects", "Craft"), Failure = "Could not find file 'Missing.csproj'." };
        notices.Observe("C#", failed, () => retried++);
        var entry = host.Notifications.Entries.Single(e => e.Source == "C#");
        Check.True(entry is { Title: "C# workspace failed", Severity: NotificationSeverity.Error } && entry.Text.Contains("is missing", StringComparison.Ordinal)
            && entry.Text.Contains("Missing.csproj", StringComparison.Ordinal) && entry.Text.Contains("Try again", StringComparison.Ordinal), $"plain words, the error and what fixes it: {entry.Text}");
        Check.True(toasts.Count == 1, "a failure toasts");
        var again = entry.Actions.Single(a => a.Label == "Try again");
        Check.True(again.Primary, "Try again is the primary action");
        await host.Notifications.RunAsync(entry.Id, again);
        Check.Equal(1, retried, "Try again builds the workspace again");
        Check.True(WorkspaceNotices.Plain("MSBuild could not be located", "").Contains(".NET SDK", StringComparison.Ordinal), "a missing SDK says to install it");

        var card = new SemanticCard("Roslyn", "Failed", "", "", false) { Failure = "boom" };
        Check.True(card is { IsFailed: true, StatusBarText: "Roslyn failed" } && card.Tooltip.Contains("boom", StringComparison.Ordinal), "the status bar shows a failure, its tooltip the reason");
        Check.Equal("Roslyn loading…", new SemanticCard("Roslyn", "Loading", "", "", false).StatusBarText, "and the loading");
        var ready = new SemanticCard("Roslyn", "Ready", "1 project", "6 MB", true) { BuildTime = "2.3 s" };
        Check.True(ready.StatusBarText.Length == 0 && ready.Tooltip == "Roslyn: 1 project, 6 MB, built in 2.3 s", "ready: nothing in the status bar; its facts in the language item's tooltip");
        Check.Equal("", new SemanticCard("Roslyn", "Waiting", "a large file", "", false).StatusBarText, "a large file waiting is not a loading state");
        Check.True(ShellText.Seconds(TimeSpan.FromMilliseconds(2300)) == "2.3 s" && ShellText.Seconds(TimeSpan.FromSeconds(64)) == "1 min 4 s" && ShellText.Seconds(TimeSpan.FromMilliseconds(120)) == "120 ms",
            "durations as the shell writes them");
    }
}

/// <summary>
/// B10: the window's global loader over the activities: workspace work (or work marked global) shown only after the
/// delay, hidden when it all ended, following the most advanced with the count, determinate with a fraction.
/// </summary>
[TestGroup("Activities")]
public static class GlobalLoaderTests
{
    [Test]
    public static void TheLoaderShowsWorkspaceWorkAfterTheDelay()
    {
        var time = new ManualTime();
        var options = new CraftOptions { ActivityMinimumMilliseconds = 0, ActivityFrameMilliseconds = 1 };
        var center = new ActivityCenter(null, options, time);
        var build = center.Start("C#: building the workspace of Craft", ActivityKinds.Workspace, keep: false);
        var chat = center.Start("Chat: hello", ActivityKinds.Generation);
        Check.True(!GlobalLoader.Of(center.Running, options).Visible, "within the delay nothing shows (quick work never flashes)");
        Check.Equal(TimeSpan.FromMilliseconds(300), GlobalLoader.DueIn(center.Running, options), "it says when to look again");
        time.AdvanceMilliseconds(300);
        var shown = GlobalLoader.Of(center.Running, options);
        Check.True(shown is { Visible: true, Fraction: null, Count: 1, Title: "C#: building the workspace of Craft" } && shown.Tooltip == shown.Title,
            "after 300 ms the workspace build shows, moving (no fraction), the chat's reply does not");
        Check.True(GlobalLoader.DueIn(center.Running, options) is null, "nothing waits any more");

        var index = center.Start("Indexing the symbols", ActivityKinds.Indexing);
        index.Global = true;
        time.AdvanceMilliseconds(400);
        index.Report(0.4);
        var both = GlobalLoader.Of(center.Running, options);
        Check.True(both is { Count: 2, Fraction: 0.4, Title: "Indexing the symbols" } && both.Tooltip == "Indexing the symbols · 40% · and 1 more",
            "work a plug-in marks global counts too; the loader follows the most advanced and counts them");
        Check.True(center.Running.Single(a => a.Title == "Indexing the symbols").Global, "the mark is on the activity's snapshot");
        build.Complete();
        index.Complete();
        Check.True(!GlobalLoader.Of(center.Running, options).Visible, "hidden once all of it ended");
        chat.Complete();

        var kinds = options with { LoaderKinds = ["generation"], LoaderDelayMilliseconds = 0 };
        center.Start("Chat: again", ActivityKinds.Generation);
        Check.True(GlobalLoader.Of(center.Running, kinds) is { Visible: true, Count: 1 }, "CRAFT_LOADER_KINDS chooses the kinds; CRAFT_LOADER_DELAY_MS 0 shows at once");
        Check.True(new CraftOptions() is { LoaderDelayMilliseconds: 300, LoaderKinds: ["workspace"] } && ActivityKinds.Workspace == "workspace", "the defaults: workspace work after 300 ms");
        var read = CraftOptions.FromVariables(n => n switch { "CRAFT_LOADER_KINDS" => "", "CRAFT_LOADER_DELAY_MS" => "50", _ => null });
        Check.True(read is { LoaderDelayMilliseconds: 50, LoaderKinds.Count: 0 }, "an empty CRAFT_LOADER_KINDS leaves only the marked work");
        Check.True(CraftOptions.Variables.Any(v => v.Name == "CRAFT_LOADER_DELAY_MS") && CraftSettings.Allows(SettingLayer.Workspace, "CRAFT_LOADER_DELAY_MS"), "the delay is on the settings page");
    }
}
