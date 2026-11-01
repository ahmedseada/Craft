// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Run;
using Craft.Editor.Search;

namespace Craft.Editor.Shell;

// X part 6: the Search view and the Run view, the shell's own side views of the rail, drawn from the public block
// contract as a plug-in's view would be; what they search and run comes from the registries (WorkspaceSearchProviders,
// RunProviders), so plug-ins extend them.
public sealed partial class ShellModel
{
    private IDisposable? _searchPanel;
    private IDisposable? _runPanel;

    /// <summary>The Search view (the rail's Search, Ctrl+Shift+F).</summary>
    public WorkspaceSearchView Search { get; private set; } = null!;

    /// <summary>The Run view (the rail's Run; F5, Ctrl+F5, Ctrl+Shift+B, Shift+F5).</summary>
    public RunView Run { get; private set; } = null!;

    /// <summary>
    /// Shows the Search view with the keyboard in its query (Ctrl+Shift+F), the active document's one-line selection as the
    /// query; with its replace field when <paramref name="replace"/> (Ctrl+Shift+H).
    /// </summary>
    public Task ShowSearch(bool replace = false)
    {
        string? selection = null;
        if (Host.ActiveDocument is { } document && document.Selection is { IsEmpty: false, Length: <= 500 } selected)
        {
            var snapshot = document.Document.Buffer.Current;
            if (selected.End <= snapshot.Length)
            {
                selection = snapshot.GetText(selected.Start, selected.Length);
            }
        }

        Search.Focus(selection, replace);
        return Layout.IsNarrow ? Task.CompletedTask : Panels.ShowSide(WorkspaceSearchView.PanelId);
    }

    private void CreateViews()
    {
        Search = new WorkspaceSearchView(Host, () => Workspace?.Root, () => Options);
        Run = new RunView(Host, () => Workspace?.Root, () => Options);
        _searchPanel = Host.AddPanel(Search);
        _runPanel = Host.AddPanel(Run);
        CreateOutline();
        WorkspaceChanged += OnViewsWorkspaceChanged;
        if (Workspace is not null)
        {
            OnViewsWorkspaceChanged(this, EventArgs.Empty);
        }
    }

    private void OnViewsWorkspaceChanged(object? sender, EventArgs e)
    {
        Search.WorkspaceChanged();
        Run.WorkspaceChanged();
        OutlineWorkspaceChanged();
    }

    // The views' commands, keys (the same as Visual Studio's and VS Code's, so both keymaps have them) and rail items.
    private void RegisterViewCommands()
    {
        Command(SearchCommands.Show, "Search: Find in files", (_, _) => ShowSearch(), null, "Ctrl+Shift+F");
        Command(SearchCommands.Replace, "Search: Replace in files", (_, _) => ShowSearch(replace: true), null, "Ctrl+Shift+H");
        Command("craft.search.rail", "View: Search", (_, _) =>
            Panels.IsOpen(ShellPanel.Explorer) && Panels.SideView == WorkspaceSearchView.PanelId ? Panels.ToggleSide(WorkspaceSearchView.PanelId) : ShowSearch(), null, null);
        Command(RunCommands.Run, "Run: Start (the default)", async (_, token) => await Run.RunDefaultAsync(RunActions.Run, token).ConfigureAwait(true), _ => Workspace is not null, "F5");
        Command(RunCommands.RunWithoutDebugging, "Run: Start without debugging (the default)", async (_, token) => await Run.RunDefaultAsync(RunActions.Run, token).ConfigureAwait(true), _ => Workspace is not null, "Ctrl+F5");
        Command(RunCommands.Build, "Run: Build (the default)", async (_, token) => await Run.RunDefaultAsync(RunActions.Build, token).ConfigureAwait(true), _ => Workspace is not null, "Ctrl+Shift+B");
        Command(RunCommands.Stop, "Run: Stop", (_, _) =>
        {
            Run.Stop();
            return Task.CompletedTask;
        }, _ => Run.Running.Count > 0, "Shift+F5");
        Menu(EditorMenus.Run, RunCommands.Run, 2, 0);
        Menu(EditorMenus.Run, RunCommands.RunWithoutDebugging, 2, 1);
        Menu(EditorMenus.Run, RunCommands.Build, 2, 2);
        Menu(EditorMenus.Run, RunCommands.Stop, 2, 3);
        Menu(EditorMenus.Edit, SearchCommands.Show, 5, 0);
        Menu(EditorMenus.Edit, SearchCommands.Replace, 5, 1);
        Rail(new RailItem(RunView.PanelId, "Run", SideViewCommand(RunView.PanelId), RailIcons.Run, 15));
        RegisterOutlineCommands();
    }

    private void DisposeViews()
    {
        WorkspaceChanged -= OnViewsWorkspaceChanged;
        _searchPanel?.Dispose();
        _runPanel?.Dispose();
        Search?.Dispose();
        Run?.Dispose();
        DisposeOutline();
    }
}
