// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core;
using Craft.Editor.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>The message a conversation's menu was opened on: the controller and the entry.</summary>
/// <param name="Controller">The conversation view's controller.</param>
/// <param name="Entry">The panel entry of the message.</param>
public sealed record ConversationMessage(ConversationController Controller, int Entry);

/// <summary>
/// The past conversations of a panel that keeps several (<see cref="IConversationHistory"/>), headless (X part 10): the
/// list filtered by a search (title, ignoring case), New, Open, Rename, and Delete with Undo through the shell's undo bar.
/// </summary>
public sealed class ConversationList
{
    private readonly UndoBar? _undo;
    private IConversationHistory? _history;
    private string _search = "";

    /// <summary>A list whose deletes offer Undo on <paramref name="undo"/> (none: a delete is final).</summary>
    public ConversationList(UndoBar? undo = null) => _undo = undo;

    /// <summary>The panel's kept conversations, or null when the panel keeps one only.</summary>
    public IConversationHistory? History
    {
        get => _history;
        set
        {
            if (ReferenceEquals(value, _history))
            {
                return;
            }

            if (_history is not null)
            {
                _history.ConversationsChanged -= OnChanged;
            }

            _history = value;
            if (value is not null)
            {
                value.ConversationsChanged += OnChanged;
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether the panel keeps several conversations (the header shows the list's button).</summary>
    public bool IsAvailable => _history is not null;

    /// <summary>The text the titles must hold (ignoring case); empty lists every one.</summary>
    public string Search
    {
        get => _search;
        set
        {
            value ??= "";
            if (!string.Equals(value, _search, StringComparison.Ordinal))
            {
                _search = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>The conversations the search lets through, the most recent first.</summary>
    public IReadOnlyList<ConversationSummary> Rows =>
        _history is null ? [] : [.. _history.Conversations.Where(c => _search.Length == 0 || c.Title.Contains(_search, StringComparison.OrdinalIgnoreCase))];

    /// <summary>The conversation shown, or null.</summary>
    public string? Current => _history?.CurrentConversation;

    /// <summary>Raised when the rows may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Starts a new conversation.</summary>
    public void New() => _history?.NewConversation();

    /// <summary>Shows the conversation <paramref name="id"/>.</summary>
    public bool Open(string id) => _history?.OpenConversation(id) ?? false;

    /// <summary>Renames the conversation <paramref name="id"/> (an empty title is ignored).</summary>
    public void Rename(string id, string title)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            _history?.RenameConversation(id, title.Trim());
        }
    }

    /// <summary>Deletes the conversation <paramref name="id"/>, with Undo on the undo bar; true when it was there.</summary>
    public bool Delete(string id)
    {
        if (_history is not { } history)
        {
            return false;
        }

        string title = history.Conversations.FirstOrDefault(c => c.Id == id)?.Title ?? "the conversation";
        if (!history.DeleteConversation(id))
        {
            return false;
        }

        _undo?.Show($"Deleted \"{title}\"", () => history.RestoreConversation(id));
        return true;
    }

    /// <summary>When <paramref name="time"/> was, from <paramref name="now"/>: "just now", "5 min ago", "3 h ago", "yesterday", else the date.</summary>
    public static string When(DateTimeOffset time, DateTimeOffset now)
    {
        var age = now - time;
        return age.TotalMinutes < 1 ? "just now"
            : age.TotalHours < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalMinutes} min ago")
            : age.TotalDays < 1 && time.Date == now.Date ? string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalHours} h ago")
            : time.Date == now.Date.AddDays(-1) ? "yesterday"
            : time.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    }

    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// One conversation view's logic, headless (X part 10, shared by the chat and the agent): the transcript, the composer and
/// the list of past conversations of the panel shown; what a click on the text runs (copy, insert at the caret as a diff,
/// run a shell block in the terminal, show a folded block, open a step, the entry's own actions); the message menu's
/// actions (copy, copy as Markdown, quote in reply, retry from here, edit and resend, delete from here); the transcript's
/// keys (Ctrl+C copies the selection as <c>CRAFT_CHAT_COPY_FORMAT</c>, Ctrl+Shift+C as the other); copy and export of the
/// whole conversation. The view draws; this decides.
/// </summary>
public sealed class ConversationController : IDisposable
{
    private readonly IEditorHost _host;
    private readonly IEditorClipboard _clipboard;
    private CraftOptions _options;
    private IConversationPanel? _panel;

    /// <summary>A controller over <paramref name="host"/>'s panels, copying through <paramref name="clipboard"/>.</summary>
    public ConversationController(IEditorHost host, CraftOptions options, IEditorClipboard clipboard, UndoBar? undo = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        Transcript = new ConversationTranscript(host.Dispatch, options);
        Composer = new ConversationComposer { Settings = ComposerOptions.From(options), Post = host.Dispatch, WorkspaceFolder = () => host.WorkspaceFolder };
        List = new ConversationList(undo);
        Transcript.ActionClicked += (_, action) => _ = RunGuardedAsync(action);
        Current = this;
    }

    /// <summary>The view's controller the palette's chat commands act on (the last made).</summary>
    public static ConversationController? Current { get; private set; }

    /// <summary>The transcript.</summary>
    public ConversationTranscript Transcript { get; }

    /// <summary>The composer.</summary>
    public ConversationComposer Composer { get; }

    /// <summary>The past conversations.</summary>
    public ConversationList List { get; }

    /// <summary>The panel shown, or null.</summary>
    public IConversationPanel? Panel
    {
        get => _panel;
        set
        {
            _panel = value;
            Transcript.Panel = value;
            Composer.Panel = value;
            Composer.CancelEdit();
            List.History = value as IConversationHistory;
        }
    }

    /// <summary>The options (the copy format, the code fold, the metrics, the composer's height).</summary>
    public CraftOptions Options
    {
        get => _options;
        set
        {
            _options = value ?? throw new ArgumentNullException(nameof(value));
            Transcript.Options = value;
            Composer.Settings = ComposerOptions.From(value);
        }
    }

    /// <summary>Whether Ctrl+C copies Markdown (<c>CRAFT_CHAT_COPY_FORMAT</c> markdown) rather than plain text.</summary>
    public bool CopiesMarkdown => !string.Equals(_options.ChatCopyFormat, "plain", StringComparison.OrdinalIgnoreCase);

    /// <summary>Raised to open a message's menu (the view opens it at the pointer).</summary>
    public event EventHandler<ConversationMessage>? MenuRequested;

    /// <summary>Raised to open a link.</summary>
    public event EventHandler<string>? LinkRequested;

    /// <summary>Raised when the composer should take the keyboard (a quote, a message to edit).</summary>
    public event EventHandler? ComposerFocusRequested;

    /// <summary>Raised with a short text for the status bar ("Copied 3 messages as Markdown").</summary>
    public event EventHandler<string>? Notice;

    /// <summary>The last action's work (the tests await it).</summary>
    public Task LastAction { get; private set; } = Task.CompletedTask;

    /// <summary>Runs <paramref name="action"/> (a click on the transcript's text).</summary>
    public Task RunAsync(TranscriptAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        switch (action.Kind)
        {
            case TranscriptActionKind.CopyMessage:
                return CopyMessageAsync(action.Entry, null);
            case TranscriptActionKind.MessageMenu:
                MenuRequested?.Invoke(this, new ConversationMessage(this, action.Entry));
                return Task.CompletedTask;
            case TranscriptActionKind.CopyCode:
                return CopyAsync(action.Text ?? "", "Copied the code block.");
            case TranscriptActionKind.InsertCode:
                return InsertAtCaretAsync(action.Text ?? "", cancellationToken);
            case TranscriptActionKind.RunCode:
                RunInTerminal(action.Text ?? "");
                return Task.CompletedTask;
            case TranscriptActionKind.ShowAll:
            case TranscriptActionKind.Toggle:
                Transcript.Toggle(action);
                return Task.CompletedTask;
            case TranscriptActionKind.Panel when action.Action is { } run:
                return run.RunAsync(cancellationToken);
            case TranscriptActionKind.Link when action.Text is { } url:
                LinkRequested?.Invoke(this, url);
                return Task.CompletedTask;
            case TranscriptActionKind.Edit:
                EditAndResend(action.Entry);
                return Task.CompletedTask;
            default:
                return Task.CompletedTask;
        }
    }

    private async Task RunGuardedAsync(TranscriptAction action)
    {
        var running = RunAsync(action);
        LastAction = running;
        try
        {
            await running.ConfigureAwait(true);
        }
#pragma warning disable CA1031 // A click's work that fails is said in the status bar and logged; the view goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Log(EditorLogLevel.Error, "A conversation action failed.", error);
            Notice?.Invoke(this, "That did not work: " + error.Message);
        }
    }

    /// <summary>Copies the message of <paramref name="entry"/> (Markdown or plain; null: <c>CRAFT_CHAT_COPY_FORMAT</c>).</summary>
    public Task CopyMessageAsync(int entry, bool? markdown) =>
        CopyAsync(Transcript.MessageText(entry, markdown ?? CopiesMarkdown), (markdown ?? CopiesMarkdown) ? "Copied the message as Markdown." : "Copied the message.");

    /// <summary>Copies the whole conversation as Markdown.</summary>
    public Task CopyConversationAsync() => CopyAsync(Transcript.ConversationMarkdown(_panel?.Title), "Copied the conversation as Markdown.");

    /// <summary>The whole conversation as a Markdown file's text (Export conversation).</summary>
    public string ExportText() => Transcript.ConversationMarkdown(_panel is { } panel ? $"{panel.Title}, {DateTimeOffset.Now.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)}" : null);

    /// <summary>Writes <see cref="ExportText"/> to <paramref name="path"/> off the UI thread; says where.</summary>
    public async Task ExportAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string text = ExportText();
        await Task.Run(() => File.WriteAllTextAsync(path, text, cancellationToken), cancellationToken).ConfigureAwait(true);
        Notice?.Invoke(this, $"Exported the conversation to {Path.GetFileName(path)}.");
    }

    /// <summary>A file name for the export of the conversation shown ("chat-2026-11-01-1430.md").</summary>
    public string ExportName() =>
        string.Create(CultureInfo.InvariantCulture, $"{(_panel?.Id ?? "chat").Replace('.', '-')}-{DateTimeOffset.Now:yyyy-MM-dd-HHmm}.md");

    /// <summary>The message of <paramref name="entry"/> quoted at the start of the composer (Quote in reply).</summary>
    public void QuoteInReply(int entry)
    {
        Composer.Quote(Transcript.Quote(entry));
        ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Retry from here: the panel asks again from <paramref name="entry"/> on.</summary>
    public Task RetryAsync(int entry, CancellationToken cancellationToken = default) =>
        _panel is { } panel && (panel.Capabilities & ConversationCapabilities.Retry) != 0 ? panel.RetryFromAsync(entry, cancellationToken) : Task.CompletedTask;

    /// <summary>Edit and resend: the user's message <paramref name="entry"/> back in the composer; Send replaces it.</summary>
    public void EditAndResend(int entry)
    {
        if (_panel is { } panel && (panel.Capabilities & ConversationCapabilities.EditAndResend) != 0 && (uint)entry < (uint)panel.Entries.Count
            && panel.Entries[entry].Role == ConversationRole.User)
        {
            Composer.Edit(entry, panel.Entries[entry].Text);
            ComposerFocusRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Delete from here: the panel drops <paramref name="entry"/> and what came after it.</summary>
    public void DeleteFrom(int entry)
    {
        if (_panel is { } panel && (panel.Capabilities & ConversationCapabilities.DeleteFrom) != 0)
        {
            panel.DeleteFrom(entry);
        }
    }

    /// <summary>Whether the message menu's <paramref name="what"/> applies to <paramref name="entry"/>.</summary>
    public bool Can(ConversationCapabilities what, int entry)
    {
        if (_panel is not { } panel || (uint)entry >= (uint)panel.Entries.Count || (panel.Capabilities & what) == 0)
        {
            return false;
        }

        return what != ConversationCapabilities.EditAndResend || panel.Entries[entry].Role == ConversationRole.User;
    }

    /// <summary>
    /// A key on the transcript before its editor takes it: Ctrl+C copies the selection in <c>CRAFT_CHAT_COPY_FORMAT</c>,
    /// Ctrl+Shift+C in the other (the editor's own Ctrl+C would copy the chrome too). True when taken.
    /// </summary>
    public bool HandleTranscriptKey(EditorModel model, EditorKey key, KeyModifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (key != EditorKey.C || (modifiers & KeyModifiers.Control) == 0 || (modifiers & KeyModifiers.Alt) != 0)
        {
            return false;
        }

        bool markdown = (modifiers & KeyModifiers.Shift) != 0 ? !CopiesMarkdown : CopiesMarkdown;
        LastAction = CopySelectionAsync(model, markdown);
        return true;
    }

    /// <summary>Copies <paramref name="model"/>'s selection from the transcript (Markdown or plain).</summary>
    public Task CopySelectionAsync(EditorModel model, bool markdown)
    {
        ArgumentNullException.ThrowIfNull(model);
        var selection = model.Selection;
        if (selection.IsEmpty)
        {
            return Task.CompletedTask;
        }

        int first = Transcript.EntryAt(selection.Start);
        int last = Transcript.EntryAt(Math.Max(selection.Start, selection.End - 1));
        int messages = first < 0 || last < 0 ? 1 : last - first + 1;
        return CopyAsync(Transcript.Copy(selection.Start, selection.End, markdown),
            string.Create(CultureInfo.InvariantCulture, $"Copied {(messages == 1 ? "the selection" : $"{messages} messages")}{(markdown ? " as Markdown" : "")}."));
    }

    /// <summary>The transcript's key handler: these keys first, then its editor's own, then the window's (as Output's).</summary>
    public IEditorKeyHandler TranscriptKeys(Func<IEditorKeyHandler?> window) => new Keys(this, new ReadOnlySurfaceKeys(window));

    private async Task CopyAsync(string text, string said)
    {
        if (text.Length == 0)
        {
            return;
        }

        await _clipboard.SetTextAsync(text.ReplaceLineEndings()).ConfigureAwait(true);
        Notice?.Invoke(this, said);
    }

    // The code goes into the active document at its caret as a diff the user accepts (no silent edit), one undo step.
    private async Task InsertAtCaretAsync(string code, CancellationToken cancellationToken)
    {
        if (_host.ActiveDocument is not { } document)
        {
            Notice?.Invoke(this, "No document is active: open the one to insert the code into.");
            return;
        }

        int caret = Math.Clamp(document.Selection.Caret, 0, document.Document.Buffer.Current.Length);
        var proposal = new ProposedEdits([TextEdit.Insert(caret, code.EndsWith('\n') ? code : code + "\n")], "Insert the conversation's code at the caret")
        {
            Placement = "at the caret",
            Origin = _panel?.Title ?? "Chat",
        };
        bool applied = await _host.ProposeAsync(document, proposal, cancellationToken).ConfigureAwait(true);
        Notice?.Invoke(this, applied ? "Inserted at the caret (one undo step)." : "Not inserted.");
    }

    // A shell block typed into the active terminal (a new one when none) and run: the user's own click.
    private void RunInTerminal(string code)
    {
        if (_host.Terminals is not { } terminals)
        {
            Notice?.Invoke(this, "This window has no terminal.");
            return;
        }

        var terminal = terminals.Active ?? terminals.Open(new EditorTerminalRequest());
        terminal.Show();
        terminal.Write(code.TrimEnd('\n', '\r').ReplaceLineEndings("\r") + "\r");
        Notice?.Invoke(this, $"Sent to the terminal {terminal.Title}.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Panel = null;
        Transcript.Dispose();
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }
    }

    private sealed class Keys(ConversationController owner, IEditorKeyHandler inner) : IEditorKeyHandler
    {
        public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor) =>
            (editor is EditorModel model && owner.HandleTranscriptKey(model, key, modifiers)) || inner.HandleKey(key, modifiers, editor);
    }
}

/// <summary>The chat's commands (X part 10): the message menu's, the panel menu's, and Expand, with their menu items.</summary>
public static class ConversationCommandIds
{
    /// <summary>Chat: Copy the message.</summary>
    public const string CopyMessage = "craft.chat.copyMessage";

    /// <summary>Chat: Copy the message as Markdown.</summary>
    public const string CopyMessageMarkdown = "craft.chat.copyMessageMarkdown";

    /// <summary>Chat: Quote in reply.</summary>
    public const string Quote = "craft.chat.quote";

    /// <summary>Chat: Retry from here.</summary>
    public const string Retry = "craft.chat.retry";

    /// <summary>Chat: Edit and resend.</summary>
    public const string EditAndResend = "craft.chat.editResend";

    /// <summary>Chat: Delete from here.</summary>
    public const string DeleteFrom = "craft.chat.deleteFrom";

    /// <summary>Chat: Copy the conversation.</summary>
    public const string CopyConversation = "craft.chat.copyConversation";

    /// <summary>Chat: Export the conversation (a Markdown file).</summary>
    public const string ExportConversation = "craft.chat.exportConversation";

    /// <summary>Chat: Show the past conversations.</summary>
    public const string Conversations = "craft.chat.conversations";

    /// <summary>Chat: New conversation.</summary>
    public const string NewConversation = "craft.chat.newConversation";

    /// <summary>Chat: Expand (the whole editor area) or restore (Ctrl+Shift+Alt+M).</summary>
    public const string Expand = "craft.chat.expand";
}

/// <summary>
/// Registers the chat's commands and their menu items (the message menu, <see cref="EditorContextMenus.Conversation"/>;
/// the panel menu, <see cref="EditorContextMenus.ConversationPanel"/>), acting on the menu's message, else the current
/// controller; Expand and the past conversations through the shell's callbacks. Disposing takes them back.
/// </summary>
public sealed class ConversationCommands : IDisposable
{
    private readonly List<string> _commands = [];
    private readonly List<(string Menu, string Id)> _items = [];

    /// <summary>Registers the commands: <paramref name="expand"/> toggles the expanded view, <paramref name="showList"/> shows the past conversations, <paramref name="export"/> asks where to export.</summary>
    public ConversationCommands(Func<Task>? expand = null, Action? showList = null, Func<ConversationController, Task>? export = null)
    {
        Add(ConversationCommandIds.CopyMessage, "Chat: Copy the message", m => m.Controller.CopyMessageAsync(m.Entry, false), static _ => true);
        Add(ConversationCommandIds.CopyMessageMarkdown, "Chat: Copy the message as Markdown", m => m.Controller.CopyMessageAsync(m.Entry, true), static _ => true);
        Add(ConversationCommandIds.Quote, "Chat: Quote in reply", m =>
        {
            m.Controller.QuoteInReply(m.Entry);
            return Task.CompletedTask;
        }, static _ => true);
        Add(ConversationCommandIds.Retry, "Chat: Retry from here", m => m.Controller.RetryAsync(m.Entry), static m => m.Controller.Can(ConversationCapabilities.Retry, m.Entry));
        Add(ConversationCommandIds.EditAndResend, "Chat: Edit and resend", m =>
        {
            m.Controller.EditAndResend(m.Entry);
            return Task.CompletedTask;
        }, static m => m.Controller.Can(ConversationCapabilities.EditAndResend, m.Entry));
        Add(ConversationCommandIds.DeleteFrom, "Chat: Delete from here", m =>
        {
            m.Controller.DeleteFrom(m.Entry);
            return Task.CompletedTask;
        }, static m => m.Controller.Can(ConversationCapabilities.DeleteFrom, m.Entry));
        Item(EditorContextMenus.Conversation, ConversationCommandIds.CopyMessage, 0, 0);
        Item(EditorContextMenus.Conversation, ConversationCommandIds.CopyMessageMarkdown, 0, 1);
        Item(EditorContextMenus.Conversation, ConversationCommandIds.Quote, 0, 2);
        Item(EditorContextMenus.Conversation, ConversationCommandIds.Retry, 1, 0);
        Item(EditorContextMenus.Conversation, ConversationCommandIds.EditAndResend, 1, 1);
        Item(EditorContextMenus.Conversation, ConversationCommandIds.DeleteFrom, 1, 2);

        Register(new EditorCommand(ConversationCommandIds.CopyConversation, "Chat: Copy the conversation", (_, _) => Controller() is { } c ? c.CopyConversationAsync() : Task.CompletedTask,
            _ => Controller() is { Panel.Entries.Count: > 0 }, "The whole conversation as Markdown on the clipboard (the speakers named, code fenced)"));
        Register(new EditorCommand(ConversationCommandIds.ExportConversation, "Chat: Export the conversation…", (_, _) => Controller() is { } c && export is not null ? export(c) : Task.CompletedTask,
            _ => export is not null && Controller() is { Panel.Entries.Count: > 0 }, "Saves the whole conversation as a Markdown file"));
        Register(new EditorCommand(ConversationCommandIds.NewConversation, "Chat: New conversation", (_, _) =>
        {
            if (Controller() is { } c)
            {
                if (c.List.IsAvailable)
                {
                    c.List.New();
                }
                else if (c.Panel?.HeaderActions.FirstOrDefault(a => a.Label is "New chat" or "Clear") is { } clear)
                {
                    return clear.RunAsync(CancellationToken.None);
                }
            }

            return Task.CompletedTask;
        }, _ => Controller()?.Panel is not null, "Starts a new conversation (the one shown stays in the list)"));
        Register(new EditorCommand(ConversationCommandIds.Conversations, "Chat: Show the past conversations", (_, _) =>
        {
            showList?.Invoke();
            return Task.CompletedTask;
        }, _ => showList is not null && Controller() is { List.IsAvailable: true }, "The conversations kept for this folder: open, rename, delete, search"));
        Register(new EditorCommand(ConversationCommandIds.Expand, "Chat: Expand", (_, _) => expand is not null ? expand() : Task.CompletedTask,
            _ => expand is not null && Controller() is not null, "The chat or agent over the whole editor area (between the side view and the window's edge); again, or Escape, restores the column"));
        Item(EditorContextMenus.ConversationPanel, ConversationCommandIds.CopyConversation, 0, 0);
        Item(EditorContextMenus.ConversationPanel, ConversationCommandIds.ExportConversation, 0, 1);
        Item(EditorContextMenus.ConversationPanel, ConversationCommandIds.NewConversation, 1, 0);
        Item(EditorContextMenus.ConversationPanel, ConversationCommandIds.Conversations, 1, 1);
        Item(EditorContextMenus.ConversationPanel, ConversationCommandIds.Expand, 2, 0);
        if (expand is not null)
        {
            // Not a keymap's key: registered with its command, so Visual Studio's keymap and VS Code's both have it.
            EditorKeyBindings.Register(ExpandBinding);
            _binding = true;
        }
    }

    /// <summary>"Chat: Expand" on Ctrl+Shift+Alt+M, anywhere (both keymaps).</summary>
    public static KeyBinding ExpandBinding { get; } = StandardShortcuts.Anywhere("Ctrl+Shift+Alt+M", ConversationCommandIds.Expand);

    private readonly bool _binding;

    /// <summary>The message the running menu was opened on, else null.</summary>
    public static ConversationMessage? Target => EditorContextMenus.Target?.Item as ConversationMessage;

    private static ConversationController? Controller() =>
        Target?.Controller ?? (EditorContextMenus.Target?.Item as ConversationController) ?? ConversationController.Current;

    private void Add(string id, string title, Func<ConversationMessage, Task> run, Func<ConversationMessage, bool> can) =>
        Register(new EditorCommand(id, title, (_, _) => Target is { } m ? run(m) : Task.CompletedTask, _ => Target is { } m && can(m), title));

    private void Register(EditorCommand command)
    {
        EditorCommands.Register(command);
        _commands.Add(command.Name);
    }

    private void Item(string menu, string id, int group, int order)
    {
        EditorMenus.Register(new MenuItem(menu, id, Order: order, Group: group));
        _items.Add((menu, id));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var id in _commands)
        {
            EditorCommands.Unregister(id);
        }

        foreach (var (menu, id) in _items)
        {
            EditorMenus.Unregister(menu, id);
        }

        _commands.Clear();
        _items.Clear();
        if (_binding)
        {
            EditorKeyBindings.Unregister(ExpandBinding);
        }
    }
}
