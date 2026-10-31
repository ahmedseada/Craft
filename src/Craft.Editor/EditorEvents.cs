// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Editor;

/// <summary>A point of the editor's content, in device-independent units from its top-left (the gutter's left edge at x 0).</summary>
/// <param name="X">From the left.</param>
/// <param name="Y">From the top.</param>
public readonly record struct EditorPoint(float X, float Y);

/// <summary>A range of lines, both ends included.</summary>
/// <param name="First">The first line's index.</param>
/// <param name="Last">The last line's index.</param>
public readonly record struct LineRange(int First, int Last)
{
    /// <summary>How many lines it holds.</summary>
    public int Count => Last - First + 1;
}

/// <summary>A bracket and its partner: the opening one's position and the closing one's.</summary>
/// <param name="Open">The opening bracket's position.</param>
/// <param name="Close">The closing bracket's position.</param>
public readonly record struct BracketMatch(int Open, int Close);

/// <summary>How many matches a find query has; <see cref="Capped"/> when counting stopped at the limit (there may be more).</summary>
/// <param name="Count">The matches counted.</param>
/// <param name="Capped">Whether counting stopped at <c>CraftOptions.FindCountLimit</c>.</param>
/// <param name="Current">The selection's match among them, from 1 ("3 of 17"); 0 when the selection is not a match.</param>
public readonly record struct FindCount(int Count, bool Capped, int Current = 0)
{
    /// <summary>"3 of 17", "17 matches" when the selection is none of them, "No results", "3 of 100,000+" when capped.</summary>
    public string Text => Count == 0 ? "No results"
        : Current > 0 ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Current:N0} of {Count:N0}{(Capped ? "+" : "")}")
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Count:N0}{(Capped ? "+" : "")} {(Count == 1 && !Capped ? "match" : "matches")}");
}

/// <summary>Where the editor asks its host to scroll to (the scroll offsets of its content).</summary>
public sealed class EditorScrollEventArgs(float x, float y) : EventArgs
{
    /// <summary>The horizontal offset.</summary>
    public float X { get; } = x;

    /// <summary>The vertical offset.</summary>
    public float Y { get; } = y;

    /// <summary>Whether the host jumps to the offsets at once instead of easing there (a log following its end).</summary>
    public bool Instant { get; init; }
}

/// <summary>The user asked to find (Ctrl+F) or to replace (Ctrl+H).</summary>
public sealed class FindRequestedEventArgs(bool replace, string? initialQuery) : EventArgs
{
    /// <summary>Whether replace was asked for (the host shows the replace field).</summary>
    public bool Replace { get; } = replace;

    /// <summary>The selected text when it is on one line (the query to start with), else null.</summary>
    public string? InitialQuery { get; } = initialQuery;
}

/// <summary>A clipboard call failed (the platform refused or threw); the editor carried on.</summary>
public sealed class ClipboardFailedEventArgs(Exception error) : EventArgs
{
    /// <summary>What went wrong.</summary>
    public Exception Error { get; } = error;
}
