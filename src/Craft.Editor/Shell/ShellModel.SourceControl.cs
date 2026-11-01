// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor.Shell;

// X part 8: what the shell shows of the plug-ins' source control: the rail items' badges (a panel's count), the open
// folder's repository's branch with ahead and behind in the status bar (its click runs the plug-in's branch command), and
// each tree row's state (its colour and letter).
public sealed partial class ShellModel
{
    private readonly Dictionary<string, string> _railBadges = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IEditorPanel> _badgePanels = [];
    private readonly List<ISourceControlRepository> _watchedRepositories = [];

    /// <summary>Raised after a rail item's badge changed (<see cref="RailBadge"/>).</summary>
    public event EventHandler? RailBadgesChanged;

    /// <summary>Raised after the open folder's repository changed its status (<see cref="SourceControl"/>).</summary>
    public event EventHandler? SourceControlChanged;

    /// <summary>The badge of the rail item <paramref name="id"/> (its panel's <see cref="IEditorPanel.Badge"/>), or null.</summary>
    public string? RailBadge(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _railBadges.TryGetValue(id, out var badge) ? badge : null;
    }

    /// <summary>The status of the repository the open folder is in (the first published for it), or null.</summary>
    public SourceControlStatus? SourceControl
    {
        get
        {
            foreach (var repository in Host.Repositories)
            {
                if (Workspace is { } workspace && Inside(workspace.Root, repository.Root))
                {
                    return repository.Status;
                }
            }

            return null;
        }
    }

    /// <summary>The status bar's branch: the repository's with ahead and behind when a plug-in published it, else the one read from <c>.git</c>.</summary>
    public string BranchText => SourceControl is { Branch: not null } status ? status.BranchText : Branch ?? "";

    /// <summary>The state of the file <paramref name="path"/> in the open folder's repository (the tree's colour and letter), or null.</summary>
    public SourceControlFile? FileState(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return SourceControl?.FileState(path);
    }

    // Called once by the constructor.
    private void WatchSourceControl()
    {
        Host.PanelsChanged += OnBadgePanelsChanged;
        Host.RepositoriesChanged += OnRepositoriesChanged;
    }

    private void UnwatchSourceControl()
    {
        Host.PanelsChanged -= OnBadgePanelsChanged;
        Host.RepositoriesChanged -= OnRepositoriesChanged;
        foreach (var panel in _badgePanels)
        {
            panel.Changed -= OnBadgePanelChanged;
        }

        foreach (var repository in _watchedRepositories)
        {
            repository.Changed -= OnRepositoryChanged;
        }

        _badgePanels.Clear();
        _watchedRepositories.Clear();
    }

    private void OnBadgePanelsChanged(object? sender, EventArgs e)
    {
        foreach (var panel in _badgePanels)
        {
            panel.Changed -= OnBadgePanelChanged;
        }

        _badgePanels.Clear();
        foreach (var panel in Host.Panels)
        {
            if (panel is not IConversationPanel)
            {
                _badgePanels.Add(panel);
                panel.Changed += OnBadgePanelChanged;
            }
        }

        ReadBadges();
    }

    private void OnBadgePanelChanged(object? sender, EventArgs e) => ReadBadges();

    // Only a badge that changed raises the event (a view redrawing its blocks raises Changed often).
    private void ReadBadges()
    {
        bool changed = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var panel in _badgePanels)
        {
            seen.Add(panel.Id);
            var badge = panel.Badge;
            if (string.IsNullOrEmpty(badge))
            {
                changed |= _railBadges.Remove(panel.Id);
            }
            else if (!_railBadges.TryGetValue(panel.Id, out var old) || old != badge)
            {
                _railBadges[panel.Id] = badge;
                changed = true;
            }
        }

        foreach (var gone in _railBadges.Keys.Where(k => !seen.Contains(k)).ToArray())
        {
            _railBadges.Remove(gone);
            changed = true;
        }

        if (changed)
        {
            RailBadgesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRepositoriesChanged(object? sender, EventArgs e)
    {
        foreach (var repository in _watchedRepositories)
        {
            repository.Changed -= OnRepositoryChanged;
        }

        _watchedRepositories.Clear();
        foreach (var repository in Host.Repositories)
        {
            _watchedRepositories.Add(repository);
            repository.Changed += OnRepositoryChanged;
        }

        SourceControlChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRepositoryChanged(object? sender, EventArgs e) => SourceControlChanged?.Invoke(this, EventArgs.Empty);

    private static bool Inside(string folder, string root)
    {
        string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return a.Equals(b, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || a.StartsWith(b + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
