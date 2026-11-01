// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor.Spelling;

/// <summary>
/// The spell check's quick fixes ("spelling" in <see cref="CodeActionProviders"/>, Ctrl+.): on an unknown word at the caret,
/// its suggestions (the user's pick types the word in its place, one undo step: the user's own choice, not a plug-in's
/// edit), Add to dictionary (the user's data folder) and Ignore in workspace (<c>.craft/words.txt</c>).
/// </summary>
/// <param name="host">The host whose documents it fixes.</param>
/// <param name="checker">The checker whose words it adds to.</param>
public sealed class SpellingCodeActions(IEditorHost host, SpellChecker checker) : ICodeActionProvider
{
    /// <summary>Its name in <see cref="CodeActionProviders"/>.</summary>
    public const string ProviderName = "spelling";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "Spelling: suggestions for an unknown word in a comment or a string, add to dictionary, ignore in workspace";

    /// <inheritdoc />
    public IReadOnlyList<CodeAction> ActionsFor(CodeActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (SpellCheckLayer.Of(context.Document.Document) is not { } layer || !layer.TryMissAt(context.Start, out int start, out int length, out string word))
        {
            return [];
        }

        var document = context.Document;
        var actions = new List<CodeAction>();
        foreach (var suggestion in checker.Suggest(word))
        {
            actions.Add(new CodeAction("spelling.change." + suggestion, $"Change '{word}' to '{suggestion}'", CodeActionKinds.Fix, _ =>
            {
                // The word is still there (the text may have moved on): it is replaced as the user's own typing.
                var snapshot = document.Document.Buffer.Current;
                if (start + length <= snapshot.Length && snapshot.GetText(start, length) == word && host.Select(document, new SelectionState(start, start + length)))
                {
                    host.TypeText(document, suggestion);
                }

                return Task.CompletedTask;
            }));
        }

        actions.Add(new CodeAction("spelling.addToUser", $"Add '{word}' to your dictionary", CodeActionKinds.Fix, async _ => Said(await checker.AddToUserAsync(word).ConfigureAwait(true))));
        actions.Add(new CodeAction("spelling.ignoreInWorkspace", $"Ignore '{word}' in this workspace (.craft/words.txt)", CodeActionKinds.Fix,
            async _ => Said(await checker.IgnoreInWorkspaceAsync(word).ConfigureAwait(true))));
        return actions;
    }

    private void Said(string? problem)
    {
        if (problem is not null)
        {
            host.Notify("Spelling", problem, NotificationSeverity.Warning);
        }
    }
}
