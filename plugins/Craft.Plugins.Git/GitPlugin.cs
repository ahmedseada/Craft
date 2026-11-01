// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Plugins.Git;
using Idrak.Abstraction.Plugins;

[assembly: IdrakPluginPackage(typeof(GitPackage))]

namespace Craft.Plugins.Git;

/// <summary>The assembly's plug-in package: what Craft registers when it loads plugins\Craft.Plugins.Git\.</summary>
public sealed class GitPackage : IIdrakPluginPackage
{
    /// <summary>The package's name (its folder's).</summary>
    public const string PackageName = "Craft.Plugins.Git";

    /// <inheritdoc />
    public string Name => PackageName;

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new GitRegistration()];
}

/// <summary>
/// Registers, each with how to take it back: "git" in <see cref="SourceControlProviders"/>, the editor plug-in "git", its
/// commands and key binding, the Source Control rail item, the tree's and the tab's git items (<see cref="EditorMenus"/>),
/// and the choices of <c>commitModel</c> (the loaded models).
/// </summary>
public sealed class GitRegistration() : IdrakPlugin(GitSettings.PluginName)
{
    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => SourceControlProviders.Register(GitPlugin.Provider), () => SourceControlProviders.Unregister(GitProvider.ProviderName));
        Part(() => EditorPlugins.Register(GitPlugin.Instance), () => EditorPlugins.Unregister(GitSettings.PluginName));
        foreach (var command in GitPlugin.Commands)
        {
            Part(() => EditorCommands.Register(command), () => EditorCommands.Unregister(command.Name));
        }

        var binding = new KeyBinding(KeyChord.Parse("Ctrl+Shift+G"), SourceControlProviders.ShowCommand);
        Part(() => EditorKeyBindings.Register(binding), () => EditorKeyBindings.Unregister(binding));
        Part(() => EditorRailItems.Register(new RailItem(SourceControlView.PanelId, "Source Control", GitPlugin.SideViewCommand, RailIcons.SourceControl, 15)),
            () => EditorRailItems.Unregister(SourceControlView.PanelId));
        Part(() => SettingChoiceSources.Register(new SettingChoices(GitPlugin.ModelChoices, () => [.. CommitMessageProviders.All.SelectMany(p => p.Models()).Where(m => m.Loaded).Select(m => m.Value).Distinct()],
            "the models loaded now (Generate commit message)")), () => SettingChoiceSources.Unregister(GitPlugin.ModelChoices));

        // The tree's and the tab's menus (X part 7 extends them): stage, unstage, discard, open changes, reveal in the view.
        int order = 0;
        foreach (var id in (string[])[GitPlugin.StageFileCommand, GitPlugin.UnstageFileCommand, GitPlugin.DiscardFileCommand, GitPlugin.OpenChangesCommand, GitPlugin.RevealCommand])
        {
            foreach (var menu in (string[])[EditorContextMenus.Tree, EditorContextMenus.Tab])
            {
                var item = new MenuItem(menu, id, Order: order, Group: 60);
                Part(() => EditorMenus.Register(item), () => EditorMenus.Unregister(menu, id));
            }

            order++;
        }

        Part(() => EditorMenus.Register(new MenuItem(EditorMenus.View, SourceControlProviders.ShowCommand, "Source Control", Group: 20)),
            () => EditorMenus.Unregister(EditorMenus.View, SourceControlProviders.ShowCommand));
    }
}

/// <summary>
/// The git plug-in ("git", X part 8): while active, one <see cref="GitSession"/> per host holds the open folder's
/// repository, the Source Control view, the gutter's marks and the commit message's draft.
/// </summary>
public sealed class GitPlugin : IEditorPlugin
{
    /// <summary>Stages the file of the tree's row or the tab (else the active document).</summary>
    public const string StageFileCommand = "git.stageFile";

    /// <summary>Unstages it.</summary>
    public const string UnstageFileCommand = "git.unstageFile";

    /// <summary>Discards its working copy's change, with Undo.</summary>
    public const string DiscardFileCommand = "git.discardFile";

    /// <summary>Shows its change in the diff view.</summary>
    public const string OpenChangesCommand = "git.openChanges";

    /// <summary>Shows the Source Control view.</summary>
    public const string RevealCommand = "git.reveal";

    /// <summary>Writes the commit message with a model already loaded.</summary>
    public const string GenerateCommand = "git.generateCommitMessage";

    /// <summary>Reviews the changes with a model already loaded (X part 11 item 4).</summary>
    public const string ReviewCommand = "git.reviewChanges";

    /// <summary>Commits the staged changes with the message in the box.</summary>
    public const string CommitCommand = "git.commit";

    /// <summary>Reads the status again now.</summary>
    public const string RefreshCommand = "git.refresh";

    /// <summary>Pulls, pushes, fetches, syncs.</summary>
    public const string PullCommand = "git.pull";

    /// <inheritdoc cref="PullCommand" />
    public const string PushCommand = "git.push";

    /// <inheritdoc cref="PullCommand" />
    public const string FetchCommand = "git.fetch";

    /// <inheritdoc cref="PullCommand" />
    public const string SyncCommand = "git.sync";

    /// <summary>The shell's command that shows the view as the side view (the rail item runs it).</summary>
    public const string SideViewCommand = "craft.sideView." + SourceControlView.PanelId;

    /// <summary>The setting choices' source of <c>commitModel</c> (the loaded models).</summary>
    public const string ModelChoices = "commit-models";

    private GitPlugin()
    {
    }

    /// <summary>The plug-in.</summary>
    public static GitPlugin Instance { get; } = new();

    /// <summary>The active session (the host the plug-in was activated in last), or null.</summary>
    public static GitSession? Session { get; private set; }

    /// <summary>"git" in <see cref="SourceControlProviders"/>, on the active host's settings and dispatcher.</summary>
    public static GitProvider Provider { get; } = new(
        () => Session is not null ? Session.Settings : new GitSettings(),
        action => (Host ?? throw new InvalidOperationException("The git plug-in is not active.")).Dispatch("git: the status changed", action));

    /// <summary>Its commands.</summary>
    public static IReadOnlyList<IEditorCommand> Commands { get; } =
    [
        Command(SourceControlProviders.ShowCommand, "Source Control: Show the view", s => { s.Show(); return Task.CompletedTask; }),
        Command(SourceControlProviders.BranchCommand, "Source Control: Switch branch", s => s.PickBranchAsync()),
        Command(GenerateCommand, "Source Control: Generate commit message", s => s.ActAsync(SourceControlView.Generate, null)),
        Command(ReviewCommand, "Source Control: Review changes", s => s.ActAsync(SourceControlView.Review, null)),
        Command(CommitCommand, "Source Control: Commit", s => s.ActAsync(SourceControlView.Commit, null)),
        Command(RefreshCommand, "Source Control: Refresh", s => s.Repository?.RefreshAsync() ?? Task.CompletedTask),
        Command(PullCommand, "Source Control: Pull", s => s.ActAsync(SourceControlView.Pull, null)),
        Command(PushCommand, "Source Control: Push", s => s.ActAsync(SourceControlView.Push, null)),
        Command(FetchCommand, "Source Control: Fetch", s => s.ActAsync(SourceControlView.Fetch, null)),
        Command(SyncCommand, "Source Control: Sync", s => s.ActAsync(SourceControlView.Sync, null)),
        FileCommand(StageFileCommand, "Source Control: Stage", (s, path) => s.StagePathAsync(path), f => !f.Staged || f.State == SourceControlState.Conflict),
        FileCommand(UnstageFileCommand, "Source Control: Unstage", (s, path) => s.UnstagePathAsync(path), f => f.Staged || Session?.Repository?.Status.Staged.Any(x => x.Path == f.Path) == true),
        FileCommand(DiscardFileCommand, "Source Control: Discard changes", (s, path) => s.DiscardPathAsync(path), f => !f.Staged && f.State != SourceControlState.Conflict),
        FileCommand(OpenChangesCommand, "Source Control: Open changes", (s, path) => s.OpenChangesAsync(path), _ => true),
        Command(RevealCommand, "Source Control: Reveal in Source Control", s => { s.Show(); return Task.CompletedTask; }),
    ];

    /// <inheritdoc />
    public string Name => GitSettings.PluginName;

    /// <inheritdoc />
    public string Summary => "Source control on your own git: the Source Control view, the changes in the gutter, the branch in the status bar, Generate commit message, Review changes";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings => GitSettings.Declared;

    private static IEditorHost? Host { get; set; }

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Host = host;
        var session = new GitSession(host);
        Session = session;
        return new Deactivation(session, host);
    }

    private static EditorCommand Command(string id, string title, Func<GitSession, Task> run) =>
        new(id, title, (_, _) => Session is { } session ? run(session) : Task.CompletedTask, _ => Session is not null);

    // A command on one file: the tree's row or the tab the menu was opened on, else the active document; it can run only
    // while that file has a change it acts on.
    private static EditorCommand FileCommand(string id, string title, Func<GitSession, string, Task> run, Func<SourceControlFile, bool> applies) =>
        new(id, title, (host, _) =>
        {
            string? path = Target(host);
            return Session is { } session && path is not null ? run(session, path) : Task.CompletedTask;
        }, host => Session is { } session && Target(host) is { } path && session.FileOf(path) is { } file && applies(file));

    private static string? Target(IEditorHost host) =>
        EditorContextMenus.Target is { Path: { } path, IsFolder: false } ? path : host.ActiveDocument?.FilePath;

    private sealed class Deactivation(GitSession session, IEditorHost host) : IDisposable
    {
        public void Dispose()
        {
            if (ReferenceEquals(Session, session))
            {
                Session = null;
            }

            if (ReferenceEquals(Host, host))
            {
                Host = null;
            }

            session.Dispose();
        }
    }
}
