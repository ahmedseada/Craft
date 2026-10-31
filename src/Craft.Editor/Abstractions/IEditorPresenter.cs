// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor.Abstractions;

/// <summary>
/// How an <see cref="EditorHost"/> asks the user: the UI implements it (Craft.Maui's page dialogs; the tests a fake).
/// A host without one rejects every proposed edit, since no edit is ever applied without the user.
/// </summary>
public interface IEditorPresenter
{
    /// <summary>Shows <paramref name="proposal"/> and asks to apply it; true when the user accepts.</summary>
    Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken);

    /// <summary>
    /// Shows <paramref name="proposal"/> (its <see cref="EditProposal.Diff"/>) and asks which hunks to apply: one answer a
    /// hunk, in order (one answer for a diff without hunks). The default asks for the whole (<see cref="ConfirmEditsAsync"/>).
    /// </summary>
    async Task<IReadOnlyList<bool>> ChooseHunksAsync(EditProposal proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        bool all = await ConfirmEditsAsync(proposal, cancellationToken).ConfigureAwait(false);
        var answers = new bool[Math.Max(1, proposal.Diff.Hunks.Count)];
        Array.Fill(answers, all);
        return answers;
    }

    /// <summary>Shows a message; completes when the user dismissed it.</summary>
    Task ShowMessageAsync(string title, string message);
}

/// <summary>
/// Where an editor's input sink sends a key first: the host decides between the user's and the plug-ins' bindings and the
/// editor's own keys (<see cref="EditorHost.HandleKey"/>).
/// </summary>
public interface IEditorKeyHandler
{
    /// <summary>Handles <paramref name="key"/> with <paramref name="modifiers"/> for <paramref name="editor"/> (the editor that has the focus, or null); true when handled.</summary>
    bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor);
}
