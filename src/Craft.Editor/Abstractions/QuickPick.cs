// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Editor.Abstractions;

/// <summary>A row of a quick pick (<see cref="IShellUi.QuickPickAsync"/>).</summary>
/// <param name="Text">What it shows first (a theme's name, an encoding).</param>
/// <param name="Detail">What it shows after, quieter (a theme's summary, where a value comes from), or null.</param>
public sealed record QuickPickItem(string Text, string? Detail = null)
{
    /// <summary>A short mark at the row's right ("current", "built-in", a plug-in's name), or null.</summary>
    public string? Mark { get; init; }
}

/// <summary>
/// A quick pick's question (X part 9): its title, its rows, the row selected first, and what to do as the selection moves
/// (the theme picker previews the theme of the row it is on).
/// </summary>
public sealed class QuickPickRequest
{
    /// <summary>A pick of one of <paramref name="items"/> under <paramref name="title"/>.</summary>
    public QuickPickRequest(string title, IReadOnlyList<QuickPickItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(items);
        Title = title;
        Items = items;
    }

    /// <summary>What is asked ("Color theme").</summary>
    public string Title { get; }

    /// <summary>The rows, in order.</summary>
    public IReadOnlyList<QuickPickItem> Items { get; }

    /// <summary>The input's placeholder ("Type to filter the themes").</summary>
    public string Placeholder { get; init; } = "";

    /// <summary>The row selected when it opens (the current theme), clamped to the rows.</summary>
    public int Selected { get; init; }

    /// <summary>Told the index of each row the selection moves to (the arrows, a filter that changes the first row), or null.</summary>
    public Action<int>? Moved { get; init; }

    /// <summary>The rows' texts (for a UI that asks with a plain list).</summary>
    public IReadOnlyList<string> Texts()
    {
        var texts = new string[Items.Count];
        for (int i = 0; i < texts.Length; i++)
        {
            texts[i] = Items[i].Text;
        }

        return texts;
    }
}
