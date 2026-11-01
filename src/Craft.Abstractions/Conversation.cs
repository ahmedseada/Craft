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

    /// <summary>
    /// The small line above a model's message: which model answered and on which device ("coder-q4 · cuda:0"), or null.
    /// The host draws it quietly above the message (X part 10).
    /// </summary>
    public string? Header { get; init; }

    /// <summary>The small line under a finished reply: its rate and time ("212 tokens · 38 tok/s · 5.6 s"), or null.</summary>
    public string? Metrics { get; init; }

    /// <summary>Whether the text is still growing (a reply streaming): the host draws a caret at its end and follows it.</summary>
    public bool IsStreaming { get; init; }

    /// <summary>
    /// What opens under a step of the agent's timeline (a tool call's arguments and output), or null: the host shows the
    /// entry collapsed to its summary line with a toggle that shows these lines.
    /// </summary>
    public string? Details { get; init; }
}

/// <summary>What a conversation panel lets the user do with its messages from the host's message menu (X part 10).</summary>
[Flags]
public enum ConversationCapabilities
{
    /// <summary>Copy and quote only (the host's own).</summary>
    None = 0,

    /// <summary>"Retry from here": <see cref="IConversationPanel.RetryFromAsync"/>.</summary>
    Retry = 1,

    /// <summary>"Edit and resend" on the user's messages: <see cref="IConversationPanel.EditAndResendAsync"/>.</summary>
    EditAndResend = 2,

    /// <summary>"Delete from here": <see cref="IConversationPanel.DeleteFrom"/>.</summary>
    DeleteFrom = 4,
}

/// <summary>A slash command the composer lists as the user types "/" ("/explain", what it does).</summary>
/// <param name="Name">Its name without the slash ("explain").</param>
/// <param name="Description">What it does, in a line.</param>
public sealed record ConversationCommand(string Name, string Description);

/// <summary>A kept conversation in a panel's list of past conversations: its id, its title (from its first message) and when it last changed.</summary>
/// <param name="Id">Its id within the panel.</param>
/// <param name="Title">Its title.</param>
/// <param name="Time">When it last changed.</param>
/// <param name="Messages">How many messages it holds.</param>
public sealed record ConversationSummary(string Id, string Title, DateTimeOffset Time, int Messages);

/// <summary>
/// A conversation panel that keeps several conversations per workspace (X part 10): the host lists them (title, time,
/// search), starts a new one, opens, renames and deletes one (with Undo: <see cref="RestoreConversation"/> brings back the
/// one deleted last). Implemented by a panel beside <see cref="IConversationPanel"/>; changes on the UI thread, and what it
/// reads or writes on disk it does off it.
/// </summary>
public interface IConversationHistory
{
    /// <summary>The kept conversations, the most recent first.</summary>
    IReadOnlyList<ConversationSummary> Conversations { get; }

    /// <summary>The id of the conversation shown, or null before its first message.</summary>
    string? CurrentConversation { get; }

    /// <summary>Starts a new, empty conversation (the shown one stays in the list).</summary>
    void NewConversation();

    /// <summary>Shows the conversation <paramref name="id"/>; false when there is none.</summary>
    bool OpenConversation(string id);

    /// <summary>Renames the conversation <paramref name="id"/>.</summary>
    void RenameConversation(string id, string title);

    /// <summary>Deletes the conversation <paramref name="id"/>; true when it was there (<see cref="RestoreConversation"/> undoes it).</summary>
    bool DeleteConversation(string id);

    /// <summary>Brings back the conversation deleted last with id <paramref name="id"/>; false when it is not held.</summary>
    bool RestoreConversation(string id);

    /// <summary>Raised on the UI thread when the list changed.</summary>
    event EventHandler? ConversationsChanged;
}

/// <summary>Context given with the next message (the active file, the selection, the problems): a chip the user may remove.</summary>
/// <param name="Id">Its id within the panel ("file", "selection", "problems", or a path).</param>
/// <param name="Text">What the chip shows ("EditorTools.cs", "Lines 18–23", "1 warning").</param>
public sealed record ContextChip(string Id, string Text)
{
    /// <summary>
    /// Why the chip cannot go with the next message (an image for a model that takes none), or null when it can: the host
    /// draws it greyed with this reason, and the panel sends nothing while it is there (the user removes it first).
    /// </summary>
    public string? Problem { get; init; }
}

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

    /// <summary>
    /// What the panel says before its first entry: what it can do and how to start (phase X's empty states); null lets the
    /// host say it from <see cref="IEditorPanel.Title"/> and <see cref="Placeholder"/>. The host adds which model answers.
    /// </summary>
    string? EmptyText => null;

    /// <summary>Whether it is working (a reply streams, a run goes on): the host shows the header's Stop.</summary>
    bool IsBusy { get; }

    /// <summary>
    /// The model role the panel answers with (<see cref="ModelRoleKeys"/>: "chat", "agent"), so the composer's model chip
    /// shows and writes that role's model and device; null for the provider's first role (the chat's).
    /// </summary>
    string? ModelRoleKey => null;

    /// <summary>The header's buttons (New chat, Stop), in order.</summary>
    IReadOnlyList<PanelAction> HeaderActions { get; }

    /// <summary>Removes the chip <paramref name="id"/> from the next message's context.</summary>
    void RemoveChip(string id);

    /// <summary>Sends what the user typed (a message, a steering note while busy); completes when it was taken, not when the reply ends.</summary>
    Task SubmitAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes files dropped on the panel (full paths: from the explorer's tree, or the system's file manager), as context of
    /// the next message (a chip each, a mention, whatever the panel does with a file); what it reads, it reads off the UI
    /// thread. Completes with how many it took; 0 by default, and the host then says the panel takes no files.
    /// </summary>
    Task<int> AcceptDropAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default) => Task.FromResult(0);

    /// <summary>Raised on the UI thread when an entry, the chips, the status, the header or the busy state changed.</summary>
    event EventHandler<ConversationChangedEventArgs>? ConversationChanged;

    /// <summary>What the host's message menu may offer beyond Copy and Quote (X part 10); none by default.</summary>
    ConversationCapabilities Capabilities => ConversationCapabilities.None;

    /// <summary>Whether the host draws the entries as a timeline of steps (the agent's run: thinking, tool calls, approvals, files).</summary>
    bool ShowsTimeline => false;

    /// <summary>The slash commands the composer lists as the user types "/"; none by default.</summary>
    IReadOnlyList<ConversationCommand> SlashCommands => [];

    /// <summary>Asks the model again from the entry at <paramref name="index"/> on (the user's message there, or the one before a reply).</summary>
    Task RetryFromAsync(int index, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Replaces the user's message at <paramref name="index"/> with <paramref name="text"/>, drops what came after it and sends it again.</summary>
    Task EditAndResendAsync(int index, string text, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Removes the entries from <paramref name="index"/> on (and what the model would remember of them).</summary>
    void DeleteFrom(int index)
    {
    }

    /// <summary>
    /// Offers the panel <paramref name="question"/> to show inline (a command waiting for approval: Approve and Deny on its
    /// entry, X part 10) instead of the host's dialog: the answer's task (an index of its choices), or null to let the host
    /// ask in its dialog (the default). Called on the UI thread.
    /// </summary>
    Task<int>? AskInline(EditorQuestion question, CancellationToken cancellationToken) => null;
}
