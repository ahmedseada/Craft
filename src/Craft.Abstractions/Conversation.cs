// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>What one entry of a conversation panel is (the host draws each its own way: the approved design's chat column).</summary>
public enum ConversationRole
{
    /// <summary>The user's message (a bubble at the end).</summary>
    User,

    /// <summary>The model's text (plain, streaming).</summary>
    Assistant,

    /// <summary>A tool call and its result in a line ("Called get_diagnostics → 1 warning").</summary>
    Tool,

    /// <summary>A code block (mono text, with its actions: "Apply as diff", and Copy when <see cref="ConversationEntry.CopyText"/> is set).</summary>
    Code,

    /// <summary>A quiet line (tokens and rate, a mode, what happened).</summary>
    Note,

    /// <summary>Something that failed.</summary>
    Error,

    /// <summary>A question to the user with its answers as actions (a command to approve).</summary>
    Approval,

    /// <summary>A section's heading ("CHANGED FILES").</summary>
    Section,

    /// <summary>A file and its change (+added −removed), with its actions.</summary>
    File,
}

/// <summary>A button of a conversation panel: its label, whether it is the primary one, and what it runs.</summary>
/// <param name="Label">What it shows ("Apply as diff").</param>
/// <param name="RunAsync">What a press runs (on the UI thread; its work off it).</param>
/// <param name="Primary">Drawn as the primary action (the accent).</param>
public sealed record PanelAction(string Label, Func<CancellationToken, Task> RunAsync, bool Primary = false);

/// <summary>One entry of a conversation panel.</summary>
/// <param name="Role">What it is.</param>
/// <param name="Text">Its text (for <see cref="ConversationRole.Tool"/> the tool's name, for a file its path).</param>
public sealed record ConversationEntry(ConversationRole Role, string Text)
{
    /// <summary>A second, quieter text (a tool's result in a line, a file's +/− counts, a command's limits), or null.</summary>
    public string? Detail { get; init; }

    /// <summary>Its buttons, in order.</summary>
    public IReadOnlyList<PanelAction> Actions { get; init; } = [];

    /// <summary>Text a Copy button puts on the clipboard (the host draws the button and copies), or null for none.</summary>
    public string? CopyText { get; init; }

    /// <summary>A code block's language as its fence named it ("csharp"), or null.</summary>
    public string? Language { get; init; }

    /// <summary>Whether it succeeded (a tool's check mark), failed (its cross), or neither (null).</summary>
    public bool? Succeeded { get; init; }
}

/// <summary>Context given with the next message (the active file, the selection, the problems): a chip the user may remove.</summary>
/// <param name="Id">Its id within the panel ("file", "selection", "problems", or a path).</param>
/// <param name="Text">What the chip shows ("EditorTools.cs", "Lines 18–23", "1 warning").</param>
public sealed record ContextChip(string Id, string Text);

/// <summary>What changed in a conversation panel.</summary>
public enum ConversationChange
{
    /// <summary>An entry was added at the end (<see cref="ConversationChangedEventArgs.Index"/>).</summary>
    EntryAdded,

    /// <summary>The entry at <see cref="ConversationChangedEventArgs.Index"/> was replaced (a streamed reply grew).</summary>
    EntryUpdated,

    /// <summary>Anything else: the entries were cleared or rebuilt, or the chips, the status, the header or the busy state changed.</summary>
    Reset,
}

/// <summary>A change of a conversation panel: what and where.</summary>
public sealed class ConversationChangedEventArgs(ConversationChange change, int index = -1) : EventArgs
{
    /// <summary>What changed.</summary>
    public ConversationChange Change { get; } = change;

    /// <summary>The entry's index for <see cref="ConversationChange.EntryAdded"/> and <see cref="ConversationChange.EntryUpdated"/>; -1 otherwise.</summary>
    public int Index { get; } = index;
}

/// <summary>
/// A panel that holds a conversation with an input line (the approved design's chat column and agent run): entries
/// that stream, context chips, a status line, header buttons, and the text the user sends. UI-free like
/// <see cref="IEditorPanel"/>, so a plug-in describes it and the host draws it (Craft's host shows these in the chat column,
/// a sheet in the narrow layout). Add it with <see cref="IEditorHost.AddPanel"/>. Raise <see cref="ConversationChanged"/>
/// on the UI thread; a streamed reply raises <see cref="ConversationChange.EntryUpdated"/> for its entry only, so the
/// host redraws one entry per piece.
/// </summary>
public interface IConversationPanel : IEditorPanel
{
    /// <summary>The header's second text ("store:coder-q4 · local"), or null.</summary>
    string? Subtitle { get; }

    /// <summary>The entries, in order.</summary>
    IReadOnlyList<ConversationEntry> Entries { get; }

    /// <summary>The context the next message carries.</summary>
    IReadOnlyList<ContextChip> Chips { get; }

    /// <summary>The status line under the entries ("212 tokens · 38 tok/s"), or null.</summary>
    string? Status { get; }

    /// <summary>The input's placeholder ("Ask about this file. @ adds context, / runs a code action.").</summary>
    string Placeholder { get; }

    /// <summary>Whether it is working (a reply streams, a run goes on): the host shows the header's Stop.</summary>
    bool IsBusy { get; }

    /// <summary>The header's buttons (New chat, Stop), in order.</summary>
    IReadOnlyList<PanelAction> HeaderActions { get; }

    /// <summary>Removes the chip <paramref name="id"/> from the next message's context.</summary>
    void RemoveChip(string id);

    /// <summary>Sends what the user typed (a message, a steering note while busy); completes when it was taken, not when the reply ends.</summary>
    Task SubmitAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Raised on the UI thread when an entry, the chips, the status, the header or the busy state changed.</summary>
    event EventHandler<ConversationChangedEventArgs>? ConversationChanged;
}
