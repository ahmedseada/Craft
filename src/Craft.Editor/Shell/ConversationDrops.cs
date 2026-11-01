// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>
/// Files dropped on a conversation panel (X part 5: the chat column takes the explorer's tree drag and the system's
/// drop): the panel's <see cref="IConversationPanel.AcceptDropAsync"/> takes them, and the host says what it could not
/// take (a panel that takes no files says so; one that took some of them says how many).
/// </summary>
public static class ConversationDrops
{
    /// <summary>
    /// Hands <paramref name="paths"/> to <paramref name="panel"/>; what the host says after it (for the status bar), or
    /// null when every path was taken or there was none. A panel's failure is said, never thrown into the drop's handler.
    /// </summary>
    public static async Task<string?> DropAsync(IConversationPanel panel, IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return null;
        }

        int taken;
        try
        {
            taken = await panel.AcceptDropAsync(paths, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
#pragma warning disable CA1031 // A plug-in's failure is said; the drop's handler goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            return $"{panel.Title} could not take the files: {error.Message}";
        }

        return Said(panel.Title, taken, paths.Count);
    }

    /// <summary>What the host says when <paramref name="title"/>'s panel took <paramref name="taken"/> of <paramref name="offered"/> dropped paths; null when it took them all.</summary>
    public static string? Said(string title, int taken, int offered) => taken <= 0
        ? $"{title} takes no files: nothing was added."
        : taken < offered
            ? string.Create(CultureInfo.InvariantCulture, $"{title} took {taken} of the {offered} dropped files (the panel says why not the rest).")
            : null;
}
