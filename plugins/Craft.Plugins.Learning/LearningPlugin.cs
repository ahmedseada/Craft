// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.Learning;
using Craft.Plugins.Learning.Abstractions;
using Idrak.Abstraction.Plugins;
using Idrak.Abstraction.Reinforcement;

[assembly: IdrakPluginPackage(typeof(LearningPackage))]

namespace Craft.Plugins.Learning;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Learning\.</summary>
public sealed class LearningPackage : IIdrakPluginPackage
{
    /// <summary>The package's name (its folder's).</summary>
    public const string PackageName = "Craft.Plugins.Learning";

    /// <inheritdoc />
    public string Name => PackageName;

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new LearningRegistration()];
}

/// <summary>
/// Registers, each with how to take it back: the Idrak libraries' built-ins and the C# coding environment
/// (<see cref="LearningLibraries"/>), the reward "editor-verdict" in Idrak's <see cref="RewardFunctions"/>, the share
/// destinations "folder" and "https", the AI events' sink "learning", the model adapter source "learning", the editor
/// plug-in "learning", its commands and its Plug-ins menu items.
/// </summary>
public sealed class LearningRegistration() : IdrakPlugin(LearningSettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(LearningLibraries.EnsureIncluded, LearningLibraries.Release);
        Part(() => RewardFunctions.Register(new EditorVerdictReward()), () => RewardFunctions.Unregister(EditorVerdictReward.RewardName));
        Part(() => ShareDestinations.Register(new FolderDestination()), () => ShareDestinations.Unregister(ShareDestinations.Folder));
        Part(() => ShareDestinations.Register(new HttpsDestination()), () => ShareDestinations.Unregister(ShareDestinations.Https));
        Part(() => AiEvents.Register(LearningSink.Instance), () => AiEvents.Unregister(LearningSink.SinkName));
        Part(() => ModelAdapters.Register(LearningAdapters.Instance), () => ModelAdapters.Unregister(LearningAdapters.SourceName));
        Part(() => EditorPlugins.Register(LearningPlugin.Instance), () => EditorPlugins.Unregister(LearningSettings.PluginName));
        // B11: the Learning view's own rail icon (a cap) in place of the shell's list icon; the shell's side-view command shows it.
        Part(() => EditorRailItems.Register(new RailItem(LearningSession.PanelId, "Learning", LearningPlugin.SideViewCommand, LearningView.LearningIcon, 20)),
            () => EditorRailItems.Unregister(LearningSession.PanelId));
        foreach (var command in LearningPlugin.Commands)
        {
            Part(() => EditorCommands.Register(command), () => EditorCommands.Unregister(command.Name));
        }

        foreach (var id in (string[])[LearningPlugin.ShowCommand, LearningPlugin.TrainCommand, LearningPlugin.WithdrawCommand])
        {
            Part(() => EditorMenus.Register(new MenuItem(EditorMenus.Plugins, id, Group: 40)), () => EditorMenus.Unregister(EditorMenus.Plugins, id));
        }
    }
}

/// <summary>The AI events' sink: hands each answer and verdict to the active session (on its host's UI thread); none active, nothing is heard.</summary>
internal sealed class LearningSink : IAiEventSink
{
    public const string SinkName = "learning";

    public static LearningSink Instance { get; } = new();

    public string Name => SinkName;

    public string Summary => "the learning plug-in: records answers and verdicts as episodes, only after your consent";

    public void Answered(AiAnswer answer) => LearningPlugin.Session?.Post(s => s.OnAnswered(answer));

    public void Judged(AiVerdict verdict) => LearningPlugin.Session?.Post(s => s.OnJudged(verdict));
}

/// <summary>The model adapter source: the active workspace's promoted adapter, for the model it was trained from.</summary>
internal sealed class LearningAdapters : IModelAdapterSource
{
    public const string SourceName = "learning";

    public static LearningAdapters Instance { get; } = new();

    public string Name => SourceName;

    public string Summary => "the learning plug-in's promoted adapter of the open workspace";

    public string? AdapterFor(string model) => LearningPlugin.Session?.AdapterFor(model);
}

/// <summary>
/// The learning plug-in ("learning"): while active, one <see cref="LearningSession"/> per host holds the consents, records
/// episodes, shows the Learning panel and the status marks, and trains.
/// </summary>
public sealed class LearningPlugin : IEditorPlugin
{
    /// <summary>Shows the Learning panel.</summary>
    public const string ShowCommand = "learning.show";

    /// <summary>Asks the learning consent (again).</summary>
    public const string ConsentCommand = "learning.consent";

    /// <summary>Withdraws the learning consent (recording stops; the user chooses whether the data is deleted).</summary>
    public const string WithdrawCommand = "learning.withdraw";

    /// <summary>Withdraws the sharing consent.</summary>
    public const string StopSharingCommand = "learning.stopSharing";

    /// <summary>Opens the learning folder.</summary>
    public const string OpenFolderCommand = "learning.openFolder";

    /// <summary>Exports the workspace's episodes.</summary>
    public const string ExportCommand = "learning.export";

    /// <summary>Deletes the workspace's episodes.</summary>
    public const string DeleteAllCommand = "learning.deleteAll";

    /// <summary>Trains now.</summary>
    public const string TrainCommand = "learning.train";

    /// <summary>Stops a training.</summary>
    public const string StopCommand = "learning.stop";

    /// <summary>Makes tasks from the workspace's failing (and marked) tests.</summary>
    public const string TasksCommand = "learning.tasksFromTests";

    /// <summary>Marks the test named at the caret as a task.</summary>
    public const string MarkTestCommand = "learning.markTest";

    /// <summary>Goes back to the adapter before the current one.</summary>
    public const string RollbackCommand = "learning.rollback";

    /// <summary>Deletes an adapter version.</summary>
    public const string DeleteVersionCommand = "learning.deleteVersion";

    /// <summary>Shares (after the preview).</summary>
    public const string ShareCommand = "learning.share";

    /// <summary>The shell's command that shows the Learning view as the side view (the rail item runs it: "craft.sideView." and the panel's id).</summary>
    public const string SideViewCommand = "craft.sideView." + LearningSession.PanelId;

    private LearningPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static LearningPlugin Instance { get; } = new();

    /// <summary>The active session (the host the plug-in was activated in last), or null.</summary>
    public static LearningSession? Session { get; private set; }

    /// <summary>Its commands.</summary>
    public static IReadOnlyList<IEditorCommand> Commands { get; } =
    [
        Command(ShowCommand, "Learning: show the panel", s => { s.Show(); return Task.CompletedTask; }),
        Command(ConsentCommand, "Learning: ask me about learning again", s => s.AskLearnAsync(again: true)),
        Command(WithdrawCommand, "Learning: stop learning (withdraw)", s => s.WithdrawAsync()),
        Command(StopSharingCommand, "Learning: stop sharing", s => s.StopSharingAsync()),
        Command(OpenFolderCommand, "Learning: open the folder", s => s.OpenFolderAsync()),
        Command(ExportCommand, "Learning: export the episodes", s => s.ExportAsync()),
        Command(DeleteAllCommand, "Learning: delete the episodes", s => s.DeleteEpisodesAsync()),
        Command(TrainCommand, "Learning: train now", s => s.TrainAsync(manual: true)),
        Command(StopCommand, "Learning: stop training", s => { s.StopTraining(); return Task.CompletedTask; }),
        Command(TasksCommand, "Learning: make tasks from the failing tests", s => s.MakeTasksAsync()),
        Command(MarkTestCommand, "Learning: mark the test at the caret as a task", s => s.MarkTestAsync()),
        Command(RollbackCommand, "Learning: roll back the adapter", s => s.RollbackAsync()),
        Command(DeleteVersionCommand, "Learning: delete an adapter version", s => s.DeleteVersionAsync()),
        Command(ShareCommand, "Learning: share (shows what goes first)", s => s.ShareAsync()),
    ];

    /// <inheritdoc />
    public string Name => LearningSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "Learning in the editor: with your consent, the AI's answers and what you did with them train your local model as an adapter";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings { get; } = PluginSettingShapes.Apply(LearningSettings.Declared, static _ => SettingSections.Learning);

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        LearningLibraries.EnsureIncluded();
        var session = new LearningSession(host);
        Session = session;
        return new Deactivation(session);
    }

    private static EditorCommand Command(string id, string title, Func<LearningSession, Task> run) =>
        new(id, title, (_, _) => Session is { } session ? run(session) : Task.CompletedTask, _ => Session is not null);

    private sealed class Deactivation(LearningSession session) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(Session, session))
            {
                Session = null;
            }

            session.Dispose();
        }
    }
}
