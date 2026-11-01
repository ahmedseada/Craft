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

    /// <summary>
    /// Shows the edits to several files as one review (X part 6: Replace all in files) titled <paramref name="title"/>, and
    /// asks which hunks of each to apply: one list of answers a proposal, one answer a hunk. The default asks for each file
    /// in turn (<see cref="ChooseHunksAsync"/>); a view that shows them together answers them together.
    /// </summary>
    async Task<IReadOnlyList<IReadOnlyList<bool>>> ChooseFilesAsync(IReadOnlyList<EditProposal> proposals, string title, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        var answers = new List<IReadOnlyList<bool>>(proposals.Count);
        foreach (var proposal in proposals)
        {
            answers.Add(await ChooseHunksAsync(proposal, cancellationToken).ConfigureAwait(true));
        }

        return answers;
    }

    /// <summary>Shows a message; completes when the user dismissed it.</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>Asks <paramref name="question"/>: the index of the answer picked, or its <see cref="EditorQuestion.Dismissed"/>; the default cannot ask (-1).</summary>
    Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken) => Task.FromResult(-1);

    /// <summary>
    /// Shows <paramref name="diff"/> of the file <paramref name="path"/> read-only (X part 8: a changed file against the
    /// index or the last commit) under <paramref name="title"/>; completes when closed. The default has no such view (false).
    /// </summary>
    Task<bool> ShowDiffAsync(string title, string path, TextDiff diff, CancellationToken cancellationToken) => Task.FromResult(false);

    /// <summary>
    /// <see cref="ShowDiffAsync(string, string, TextDiff, CancellationToken)"/> with <paramref name="notes"/> under their
    /// new lines (X part 11: a review's findings). The default shows the diff alone.
    /// </summary>
    Task<bool> ShowDiffAsync(string title, string path, TextDiff diff, IReadOnlyList<DiffNote> notes, CancellationToken cancellationToken) =>
        ShowDiffAsync(title, path, diff, cancellationToken);
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
