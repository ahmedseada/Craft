// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Idrak.Text.Abstractions;

namespace Craft.Ai;

/// <summary>
/// Where a model's snippet goes in a document (plan 1, "Code actions": "a model's snippet is placed by a TextMatchers
/// matcher"): the span of the text closest to it by the setting's Idrak matcher (<see cref="AiSettings.Placement"/>,
/// "semi-global" by default; P4 registers a C#-aware one) when it differs in at most
/// <see cref="AiSettings.PlacementMaxError"/> of its characters; else the selection; else the caret. The result says how,
/// for the diff view. Off the UI thread: matching is quadratic in the sizes, so a text and snippet whose product passes
/// <see cref="MatchWorkLimit"/> skip the matcher.
/// </summary>
public static class SnippetPlacement
{
    /// <summary>The most characters of text times characters of snippet a match is tried over.</summary>
    public const long MatchWorkLimit = 400_000_000;

    /// <summary>The edit that puts <paramref name="snippet"/> into <paramref name="snapshot"/>, and how it was placed.</summary>
    /// <exception cref="NotRegisteredException">The setting names a matcher that is not registered.</exception>
    public static (TextEdit Edit, string How) Place(ITextSnapshot snapshot, SelectionState selection, string snippet, AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snippet);
        ArgumentNullException.ThrowIfNull(settings);
        string draft = snippet.TrimEnd('\n', '\r');
        string text = snapshot.GetText(0, snapshot.Length);
        if (draft.Length > 0 && (long)draft.Length * text.Length <= MatchWorkLimit && text.Length > 0)
        {
            var matcher = TextMatchers.Get(settings.Placement);
            if (matcher.Best(draft, text, 0, new TextMatchOptions()) is { } match && match.Length > 0
                && match.Distance <= settings.PlacementMaxError * draft.Length)
            {
                string how = match.Distance == 0
                    ? $"{matcher.Name}: the same text"
                    : string.Create(CultureInfo.InvariantCulture, $"{matcher.Name}: the closest span, {match.Distance} characters differed");
                return (new TextEdit(match.Start, match.Length, draft), how);
            }
        }

        if (!selection.IsEmpty)
        {
            int start = Math.Clamp(selection.Start, 0, snapshot.Length);
            int end = Math.Clamp(selection.End, start, snapshot.Length);
            string replacement = snapshot.GetText(start, end - start).EndsWith('\n') && !snippet.EndsWith('\n') ? snippet + "\n" : snippet;
            return (new TextEdit(start, end - start, replacement), "in place of the selection (no span of the document was close enough)");
        }

        int caret = Math.Clamp(selection.Caret, 0, snapshot.Length);
        return (new TextEdit(caret, 0, snippet), "at the caret (no span of the document was close enough)");
    }
}
