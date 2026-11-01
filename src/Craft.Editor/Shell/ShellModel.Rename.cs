// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>What a key does in the rename box.</summary>
public enum RenameKeyAction
{
    /// <summary>The box's own (typing, moving in the name).</summary>
    None,

    /// <summary>Enter: the rename is computed and proposed.</summary>
    Commit,

    /// <summary>Escape: the box closes, nothing renamed.</summary>
    Cancel,
}

/// <summary>
/// A rename the user started with F2 (the X restart's piece 3b): the editor, the snapshot and the name the language said
/// can be renamed; the shell shows the rename box over that name until it is committed or cancelled.
/// </summary>
public sealed class RenameRequest
{
    internal RenameRequest(EditorModel editor, ISemanticLanguageSession session, ITextSnapshot snapshot, RenameTarget target)
    {
        Editor = editor;
        Session = session;
        Snapshot = snapshot;
        Target = target;
    }

    /// <summary>The editor it started in (the box sits over its name).</summary>
    public EditorModel Editor { get; }

    /// <summary>The language session that renames.</summary>
    public ISemanticLanguageSession Session { get; }

    /// <summary>The text the name was found in.</summary>
    public ITextSnapshot Snapshot { get; }

    /// <summary>The name, its place and its text.</summary>
    public RenameTarget Target { get; }

    /// <summary>What <paramref name="key"/> does in the box: Enter commits, Escape cancels, the rest is the box's.</summary>
    public static RenameKeyAction ActionFor(EditorKey key, KeyModifiers modifiers) => (key, modifiers) switch
    {
        (EditorKey.Enter, KeyModifiers.None) => RenameKeyAction.Commit,
        (EditorKey.Escape, KeyModifiers.None) => RenameKeyAction.Cancel,
        _ => RenameKeyAction.None,
    };
}

/// <summary>How a rename ended: whether it was applied, in how many files, and what the shell said.</summary>
/// <param name="Applied">Whether the user accepted and the edits were applied.</param>
/// <param name="Files">The files changed.</param>
/// <param name="Message">What was said ("Renamed in 3 files", the language's refusal, "Rejected").</param>
public sealed record RenameOutcome(bool Applied, int Files, string Message);

public sealed partial class ShellModel
{
    /// <summary>Raised (on the UI thread) when F2 found a name to rename: the view shows the rename box over it.</summary>
    public event EventHandler<RenameRequest>? RenameRequested;

    private IEditorNotification? _renameStatus;

    /// <summary>The rename whose box is open, or null.</summary>
    public RenameRequest? PendingRename { get; private set; }

    /// <summary>
    /// F2: asks the focused editor's language, off the UI thread, what the symbol at the caret is; when it can be renamed,
    /// raises <see cref="RenameRequested"/> (the box opens over its name). Said in the status when there is nothing to rename,
    /// or why it cannot be. Null when no box opens.
    /// </summary>
    public async Task<RenameRequest?> BeginRenameAsync(CancellationToken cancellationToken = default)
    {
        if (Tabs.Active is not { } tab || EditorOf?.Invoke(tab) is not { SemanticSession: { } session } editor)
        {
            return null;
        }

        var snapshot = editor.Snapshot;
        int caret = editor.Selection.Caret;
        RenameTarget? target;
        try
        {
            target = await session.PrepareRenameAsync(snapshot, caret, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        if (target is null)
        {
            Say("There is no symbol at the caret to rename.");
            return null;
        }

        if (target.Refusal is { } refusal)
        {
            Say(refusal);
            return null;
        }

        if (editor.Snapshot.Version != snapshot.Version)
        {
            Say("The text changed while the symbol was looked up; press F2 again.");
            return null;
        }

        var request = new RenameRequest(editor, session, snapshot, target);
        PendingRename = request;
        RenameRequested?.Invoke(this, request);
        return request;
    }

    /// <summary>Escape in the box: nothing is renamed.</summary>
    public void CancelRename() => PendingRename = null;

    /// <summary>
    /// Enter in the box: the edits that rename <paramref name="request"/>'s symbol to <paramref name="newName"/> computed off
    /// the UI thread as the activity "Rename" (cancellable), then proposed as one diff of every file
    /// (<see cref="EditorHost.ProposeWorkspaceEditAsync"/>); the conflicts the rename would make are said in the diff's title
    /// and the notifications. Accepted, the Undo bar says "Renamed in N files" and takes the whole change back.
    /// </summary>
    public async Task<RenameOutcome> CommitRenameAsync(RenameRequest request, string newName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(newName);
        if (ReferenceEquals(PendingRename, request))
        {
            PendingRename = null;
        }

        string name = newName.Trim();
        string old = request.Target.Name;
        if (name.Length == 0 || string.Equals(name, old, StringComparison.Ordinal))
        {
            return new RenameOutcome(false, 0, "The name is unchanged.");
        }

        if (request.Editor.Snapshot.Version != request.Snapshot.Version)
        {
            return Said(new RenameOutcome(false, 0, "The text changed since F2; press F2 again."));
        }

        WorkspaceEdit edit;
        using (var activity = Host.Activities.Start("Rename", ActivityKinds.General, cancellable: true, cancellationToken))
        {
            try
            {
                edit = await request.Session.RenameAsync(request.Snapshot, request.Target.Start, name, activity.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                return new RenameOutcome(false, 0, "The rename was cancelled.");
            }

            activity.Complete(string.Create(CultureInfo.InvariantCulture, $"{edit.EditCount} {(edit.EditCount == 1 ? "edit" : "edits")} in {edit.Files.Count} {(edit.Files.Count == 1 ? "file" : "files")}"));
        }

        if (edit.Refusal is { } refusal)
        {
            return Said(new RenameOutcome(false, 0, refusal));
        }

        if (edit.Files.Count == 0)
        {
            return Said(new RenameOutcome(false, 0, $"Nothing names {old}: nothing to change."));
        }

        string title = $"Rename {old} to {name}";
        if (edit.Conflicts.Count > 0)
        {
            title += string.Create(CultureInfo.InvariantCulture, $" ({edit.Conflicts.Count} {(edit.Conflicts.Count == 1 ? "conflict" : "conflicts")}: {edit.Conflicts[0]})");
            Host.Notify("Rename", $"{old} to {name} would make {edit.Conflicts.Count} {(edit.Conflicts.Count == 1 ? "problem" : "problems")}:\n" + string.Join('\n', edit.Conflicts),
                NotificationSeverity.Warning);
        }

        var result = await Host.ProposeWorkspaceEditAsync(edit, title, cancellationToken);
        if (result is not { Files: > 0 } applied)
        {
            return Said(new RenameOutcome(false, 0, Host.LastWorkspaceEditRefusal ?? $"{title}: nothing was changed."));
        }

        string text = applied.Files == 1 ? "Renamed in 1 file" : string.Create(CultureInfo.InvariantCulture, $"Renamed in {applied.Files} files");
        UndoBar.Show(text, () => _ = applied.UndoAsync(), applied.Keep);
        return new RenameOutcome(true, applied.Files, text);
    }

    // One status line for the rename, replaced by the next (a sticky status would pile up otherwise).
    private void Say(string text)
    {
        _renameStatus?.Dispose();
        _renameStatus = Host.ShowStatus("Rename", text);
    }

    private RenameOutcome Said(RenameOutcome outcome)
    {
        Say(outcome.Message);
        return outcome;
    }

    private void RegisterSymbolCommands()
    {
        Command(ShellCommandIds.RenameSymbol, "Refactor: Rename symbol", (_, token) => BeginRenameAsync(token),
            _ => Tabs.Active is { } tab && EditorOf?.Invoke(tab)?.SemanticSession is not null, "F2");
        Command(ShellCommandIds.GoToSymbol, "Go: Go to a symbol in the editor ('@')", (_, _) => View(ShellViewNames.GoToSymbol), _ => Tabs.Active is not null, "Ctrl+Shift+O");
    }
}
