// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>What a gutter mark says about its lines (X part 8: the lines' change against a repository's index).</summary>
public enum LineMarkKind
{
    /// <summary>Lines added (the success colour).</summary>
    Added,

    /// <summary>Lines changed (the info colour).</summary>
    Changed,

    /// <summary>Lines removed before <see cref="LineMark.Line"/> (the error colour, a wedge between two lines).</summary>
    Deleted,

    /// <summary>A merge conflict's block (the warning colour).</summary>
    Conflict,

    /// <summary>A test that can run (X part 9): a ▶ in the gutter's left; a click runs it.</summary>
    TestReady,

    /// <summary>A test that passed its last run: a ✓ in the success colour.</summary>
    TestPassed,

    /// <summary>A test that failed its last run: a ✗ in the error colour.</summary>
    TestFailed,

    /// <summary>A test skipped in its last run: a ○ in the warning colour.</summary>
    TestSkipped,
}

/// <summary>A mark in an editor's gutter and on its overview ruler: lines and what they are.</summary>
/// <param name="Line">The first line (0-based); for <see cref="LineMarkKind.Deleted"/> the line the removed ones stood before.</param>
/// <param name="Count">How many lines it covers; for <see cref="LineMarkKind.Deleted"/> how many were removed (it covers none).</param>
/// <param name="Kind">What it is.</param>
public readonly record struct LineMark(int Line, int Count, LineMarkKind Kind)
{
    /// <summary>Whether it is drawn as a glyph at the gutter's left (a test's ▶, ✓, ✗ or ○) rather than a bar in the marks' strip.</summary>
    public bool IsGlyph => Kind >= LineMarkKind.TestReady;

    /// <summary>The last line it covers (its <see cref="Line"/> for a deletion).</summary>
    public int LastLine => Kind == LineMarkKind.Deleted ? Line : Line + Math.Max(1, Count) - 1;
}

/// <summary>A gutter mark was clicked (<see cref="IEditorHost.LineMarkClicked"/>): the document, the marks' source and the mark.</summary>
public sealed class LineMarkEventArgs(IEditorDocument document, string source, LineMark mark) : EventArgs
{
    /// <summary>The document.</summary>
    public IEditorDocument Document { get; } = document;

    /// <summary>Who set the marks (<see cref="IEditorHost.SetLineMarks"/>'s source).</summary>
    public string Source { get; } = source;

    /// <summary>The mark clicked.</summary>
    public LineMark Mark { get; } = mark;
}

/// <summary>A button of an <see cref="InlineCard"/>: its title and what it does (on the UI thread; the card closes first).</summary>
/// <param name="Title">What it says ("Revert", "Stage").</param>
/// <param name="Run">What it does.</param>
public sealed record InlineCardAction(string Title, Action Run);

/// <summary>
/// A card shown in the editor under a line (<see cref="IEditorHost.ShowInlineCard"/>): a change's old and new lines, a
/// conflict's choices. A line of <see cref="Text"/> that starts with "- " is drawn as removed, "+ " as added. It stays until
/// Escape, a press outside it, or one of its actions.
/// </summary>
/// <param name="Line">The line it is shown under (0-based).</param>
/// <param name="Text">Its lines, separated by '\n'.</param>
/// <param name="Actions">Its buttons, in order.</param>
public sealed record InlineCard(int Line, string Text, IReadOnlyList<InlineCardAction> Actions);
