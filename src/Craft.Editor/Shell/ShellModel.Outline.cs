// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Workspaces;
using Craft.Editor.Outline;
using Craft.Editor.Run;

namespace Craft.Editor.Shell;

// X part 9: the Outline view (a side view of the rail), the call and type hierarchies (a tab of the bottom panel), the
// user's snippets (the "user" snippet provider) and the code lenses' Run test (through the Run view).
public sealed partial class ShellModel
{
    private IDisposable? _outlinePanel;
    private UserSnippets? _userSnippets;

    /// <summary>The Outline view (the rail's Outline).</summary>
    public OutlineView Outline { get; private set; } = null!;

    /// <summary>The call and type hierarchies (the bottom panel's Hierarchy tab).</summary>
    public HierarchyView Hierarchy { get; private set; } = null!;

    /// <summary>The user's snippets (registered as the "user" snippet provider while the shell lives).</summary>
    public UserSnippets UserSnippets => _userSnippets!;

    private void CreateOutline()
    {
        Outline = new OutlineView(Host, () => Tabs.Active is { } tab ? EditorOf?.Invoke(tab) : null, () => Options);
        _outlinePanel = Host.AddPanel(Outline);
        Hierarchy = new HierarchyView(Host, () => Options, GoToPlaceAsync);
        _userSnippets = new UserSnippets();
        SnippetProviders.Register(_userSnippets);
        _ = _userSnippets.SetFolders(Options.ResolveSnippetsFolder(), Workspace?.Root, Options.WorkspaceWatcher);
        Host.DocumentSaved += OnSnippetsSaved;
        Host.RunTests = async (file, test, token) => (await Run.RunTestAsync(file, test, token).ConfigureAwait(true)).End != RunEnd.NotStarted;
    }

    private void OutlineWorkspaceChanged()
    {
        _ = _userSnippets?.SetFolders(Options.ResolveSnippetsFolder(), Workspace?.Root, Options.WorkspaceWatcher);
        Outline.Refresh();
    }

    private void OnSnippetsSaved(object? sender, EditorDocumentEventArgs e) => _userSnippets?.NoteSaved(e.Document.Document.FilePath);

    // The commands: the Outline as a side view, the hierarchies (Shift+Alt+H in both keymaps; the type hierarchy in the
    // palette and the editor's menu).
    private void RegisterOutlineCommands()
    {
        Command(ShellCommandIds.ShowOutline, "View: Outline", (_, _) =>
        {
            Outline.Refresh();
            return Panels.ToggleSide(OutlineView.PanelId);
        }, null, null);
        Command(ShellCommandIds.CallHierarchy, "Go: Call hierarchy", (_, token) => ShowHierarchyAsync(HierarchyKind.Call, token), _ => ActiveSemantic() is not null, "Shift+Alt+H",
            _ => "The file's language has no semantic layer (C# has).");
        Command(ShellCommandIds.TypeHierarchy, "Go: Type hierarchy", (_, token) => ShowHierarchyAsync(HierarchyKind.Type, token), _ => ActiveSemantic() is not null, null,
            _ => "The file's language has no semantic layer (C# has).");
        Menu(EditorContextMenus.Editor, ShellCommandIds.CallHierarchy, 1, 2);
        Menu(EditorContextMenus.Editor, ShellCommandIds.TypeHierarchy, 1, 3);
        Rail(new RailItem(OutlineView.PanelId, "Outline", SideViewCommand(OutlineView.PanelId), RailIcons.Outline, 12));
    }

    private EditorModel? ActiveSemantic() => Tabs.Active is { } tab && EditorOf?.Invoke(tab) is { SemanticSession: not null } model ? model : null;

    /// <summary>Shows the <paramref name="kind"/> hierarchy of the symbol at the active editor's caret in the bottom panel's Hierarchy tab.</summary>
    public Task ShowHierarchyAsync(HierarchyKind kind, CancellationToken cancellationToken = default)
    {
        if (ActiveSemantic() is not { SemanticSession: { } session } model)
        {
            return Task.CompletedTask;
        }

        ViewRequested?.Invoke(this, ShellViewNames.Hierarchy);
        return Hierarchy.ShowAsync(session, model.Snapshot, model.Selection.Caret, kind, cancellationToken);
    }

    // A hierarchy's click: the file opened (null: the active one) with the caret at the line and column, from 1.
    private async Task GoToPlaceAsync(string? path, int line, int column, CancellationToken cancellationToken)
    {
        var tab = Tabs.Active;
        if (path is not null && !PathKeys.Comparer.Equals(Path.GetFullPath(path), tab?.FilePath is { } open ? Path.GetFullPath(open) : ""))
        {
            tab = await OpenFileAsync(path, cancellationToken: cancellationToken).ConfigureAwait(true);
        }

        if (tab is not null)
        {
            Tabs.Activate(tab);
            EditorOf?.Invoke(tab)?.GoToLine(line, column);
        }
    }

    private void DisposeOutline()
    {
        _outlinePanel?.Dispose();
        Outline?.Dispose();
        Host.DocumentSaved -= OnSnippetsSaved;
        Host.RunTests = null;
        if (_userSnippets is { } snippets)
        {
            if (ReferenceEquals(SnippetProviders.Find(UserSnippets.ProviderName), snippets))
            {
                SnippetProviders.Unregister(UserSnippets.ProviderName);
            }

            snippets.Dispose();
        }
    }
}
