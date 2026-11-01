// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;

namespace Craft.Ai;

/// <summary>
/// The chat panel (plan 1 phase A2, the approved design's chat column; a sheet in the narrow layout): the chat model of the
/// AI plug-in's provider (<see cref="AiModels.ChatAsync"/>) with the editor's live tools offered (Idrak's
/// <see cref="ChatTools.WithTools"/> over <see cref="EditorToolSets.Live"/>), the reply streamed into one entry at a time,
/// each tool call a row ("Called get_diagnostics → 1 warning"), each fenced code block an entry with "Apply as diff"
/// (placed by <see cref="SnippetPlacement"/>, proposed through <see cref="IEditorHost.ProposeAsync"/>: the diff view) and
/// Copy, then the tokens and the rate. Context chips: the active file, the selection, its problems (each removable for the
/// next message); "@selection", "@problems", "@file" or "@PATH" add one; "/explain", "/fix", "/doc", "/refactor",
/// "/tests" run a code action. Stop cancels the reply. The conversation is kept per workspace
/// (<see cref="AiSettings.ChatKeep"/>, <see cref="AiSettings.ChatFile"/>). State changes on the UI thread; the model runs
/// on the thread pool.
/// </summary>
public sealed class AiChatPanel : ConversationPanel, IConversationHistory, IModelSessionHost, IDisposable
{
    /// <summary>The panel's id.</summary>
    public const string PanelId = "ai.chat";

    private const string SystemPrompt = "You are a coding assistant inside Craft, a code editor. Answer briefly and exactly. "
                                        + "Use the editor's tools to read documents, the selection and the diagnostics when you need them. "
                                        + "Put code the user may apply in one fenced block per change, with only the code that replaces or is added.";

    private readonly Func<AiSettings> _settings;
    private readonly AiModels _models;
    private readonly IToolRegistry _tools;
    private readonly List<Turn> _turns = [];                 // the conversation's exchanges: the user's entry, what was sent, the reply
    private readonly List<KeptConversation> _others = [];    // the workspace's other conversations (X part 10), the shown one excluded
    private readonly Dictionary<string, KeptConversation> _deleted = new(StringComparer.Ordinal);   // deleted, kept for Undo
    private string? _currentId;
    private string _currentTitle = "";
    private bool _titleSet;
    private DateTimeOffset _currentTime;
    private string _header = "";
    private readonly HashSet<string> _removed = new(StringComparer.Ordinal);
    private readonly List<ContextChip> _extra = [];
    private readonly Dictionary<string, ChatImage> _images = new(StringComparer.Ordinal);   // an image chip's id → its bytes, read off the UI thread at the drop
    private readonly Dictionary<ToolCall, int> _toolRows = new(ReferenceEqualityComparer.Instance);
    private readonly StringBuilder _segment = new();
    private readonly StringBuilder _reply = new();
    private readonly Lock _incomingGate = new();
    private readonly StringBuilder _incoming = new();
    private int _incomingGeneration;
    private bool _incomingPending;
    private Task _writes = Task.CompletedTask;
    private CancellationTokenSource? _running;
    private int _generation;
    private int _segmentIndex = -1;
    private int _replyStart;
    private string? _loadedFolder;
    private string _modelName = "";
    private string _lastPrompt = "";
    private bool _disposed;
    private int _keptGeneration;
    private IReadOnlySet<string>? _partKinds;       // what the chat's model took at its last reply (its part kinds), and which model it was
    private string _partKindsModel = "";

    /// <summary>A chat panel in <paramref name="host"/>.</summary>
    public AiChatPanel(IEditorHost host, Func<AiSettings> settings, AiModels models)
        : base(host, PanelId, "Chat")
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        // The owner, 2026-11-01 ("2a"): each conversation's own model settings, kept with it in the chat's file.
        _session = new SessionModel(host, settings, models, ModelRoleKeys.Chat, "this chat");
        _session.Changed += (_, _) =>
        {
            if (!_restoring)
            {
                Save();                                                  // the user's change is kept; a kept one shown again is not written back
            }

            ModelSessionChanged?.Invoke(this, EventArgs.Empty);
        };
        _tools = ToolRegistry.Create().Add(EditorToolSets.Live(host)).Build();   // every registered tool set (the tools plug-in's "editor", and any plug-in's)
        host.ActiveDocumentChanged += OnContextChanged;
        host.SelectionChanged += OnContextChanged;
        LoadIfNeeded();
        RefreshChips();
    }

    private readonly SessionModel _session;
    private bool _restoring;

    /// <inheritdoc />
    public string ModelSessionTitle => _session.Title;

    /// <inheritdoc />
    public ModelSessionSettings ModelSession => _session.Settings;

    /// <inheritdoc />
    public ModelSessionShared ModelShared => _session.Shared;

    /// <summary>The conversation's own model settings and lease (the tests' view).</summary>
    public SessionModel Session => _session;

    /// <inheritdoc />
    public event EventHandler? ModelSessionChanged;

    /// <inheritdoc />
    public Task<ModelMemoryEstimate?> EstimateModelSessionAsync(ModelSessionSettings proposed, CancellationToken cancellationToken = default) =>
        _session.EstimateAsync(proposed, cancellationToken);

    /// <inheritdoc />
    public Task<bool> SaveModelSessionAsync(ModelSessionSettings proposed, CancellationToken cancellationToken = default) => _session.SaveAsync(proposed, cancellationToken);

    /// <inheritdoc />
    public Task<string> ApplyModelSessionToEnvironmentAsync(ModelSessionSettings proposed, ModelSettingsScope scope, CancellationToken cancellationToken = default) =>
        _session.ApplyToEnvironmentAsync(proposed, scope, cancellationToken);

    /// <summary>Runs a slash command's code action ("explain", "fix", "doc", "refactor", "tests"); set by the session; true when one ran.</summary>
    public Func<string, CancellationToken, Task<bool>>? RunAction { get; set; }

    /// <summary>The reply streaming now, or the last one (the tests await it); completes when it ended, was stopped or failed.</summary>
    public Task LastReply { get; private set; } = Task.CompletedTask;

    /// <summary>The messages kept (user and assistant, the answered exchanges), oldest first: what goes back to the model.</summary>
    public IReadOnlyList<ChatMessage> History => HistoryMessages();

    /// <summary>The chat's message menu: Retry from here, Edit and resend, Delete from here (X part 10).</summary>
    public override ConversationCapabilities Capabilities =>
        ConversationCapabilities.Retry | ConversationCapabilities.EditAndResend | ConversationCapabilities.DeleteFrom;

    /// <inheritdoc />
    public override IReadOnlyList<ConversationCommand> SlashCommands { get; } =
    [
        new("explain", "Explain the selection, or the problem at the caret"),
        new("fix", "Fix the problem at the caret (a diff to accept)"),
        new("doc", "Add doc comments to the selection"),
        new("refactor", "Refactor the selection"),
        new("tests", "Write tests for the selection"),
        new("clear", "Start a new conversation (this one stays in the list)"),
    ];

    /// <inheritdoc />
    public event EventHandler? ConversationsChanged;

    /// <inheritdoc />
    public IReadOnlyList<ConversationSummary> Conversations
    {
        get
        {
            var list = new List<ConversationSummary>(_others.Count + 1);
            if (_currentId is not null)
            {
                list.Add(new ConversationSummary(_currentId, _currentTitle, _currentTime, _turns.Count));
            }

            list.AddRange(_others.Select(c => new ConversationSummary(c.Id, c.Title, c.Time, c.Turns.Count)));
            return [.. list.OrderByDescending(c => c.Time)];
        }
    }

    /// <inheritdoc />
    public string? CurrentConversation => _currentId;

    /// <inheritdoc />
    public void NewConversation()
    {
        Stop();
        Capture();
        StartEmpty();
        Save();
        ConversationsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public bool OpenConversation(string id)
    {
        if (string.Equals(id, _currentId, StringComparison.Ordinal))
        {
            return true;
        }

        if (_others.FirstOrDefault(c => c.Id == id) is not { } target)
        {
            return false;
        }

        Stop();
        Capture();
        _others.Remove(target);
        Show(target);
        Save();
        ConversationsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <inheritdoc />
    public void RenameConversation(string id, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (string.Equals(id, _currentId, StringComparison.Ordinal))
        {
            _currentTitle = title.Trim();
            _titleSet = true;
        }
        else if (_others.FirstOrDefault(c => c.Id == id) is { } other)
        {
            other.Title = title.Trim();
            other.TitleSet = true;
        }
        else
        {
            return;
        }

        Save();
        ConversationsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public bool DeleteConversation(string id)
    {
        if (string.Equals(id, _currentId, StringComparison.Ordinal))
        {
            Stop();
            _deleted[id] = CaptureCurrent();
            if (_others.OrderByDescending(c => c.Time).FirstOrDefault() is { } next)
            {
                _others.Remove(next);
                Show(next);
            }
            else
            {
                StartEmpty();
            }
        }
        else if (_others.FirstOrDefault(c => c.Id == id) is { } other)
        {
            _others.Remove(other);
            _deleted[id] = other;
        }
        else
        {
            return false;
        }

        Save();
        ConversationsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <inheritdoc />
    public bool RestoreConversation(string id)
    {
        if (!_deleted.Remove(id, out var kept))
        {
            return false;
        }

        _others.Add(kept);
        Save();
        ConversationsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Retry from here (X part 10): the exchange holding <paramref name="index"/> and every later one go, and its message
    /// is asked again (with the context of now).
    /// </summary>
    public override Task RetryFromAsync(int index, CancellationToken cancellationToken = default)
    {
        if (TurnOf(index) is not { } turn || (uint)turn.Entry >= (uint)Entries.Count)
        {
            return Task.CompletedTask;
        }

        string message = Entries[turn.Entry].Text;
        Stop();
        Truncate(turn, keepUser: false);
        return AskAsync(message, cancellationToken);
    }

    /// <summary>Edit and resend: the user's message at <paramref name="index"/> replaced by <paramref name="text"/>, what came after it dropped, and asked.</summary>
    public override Task EditAndResendAsync(int index, string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (_turns.FirstOrDefault(t => t.Entry == index) is not { } turn)
        {
            return Task.CompletedTask;
        }

        Stop();
        Truncate(turn, keepUser: false);
        return AskAsync(text.Trim(), cancellationToken);
    }

    /// <summary>
    /// Delete from here: from the user's message, its exchange and every later one go; from a reply, that reply and every
    /// later exchange (the message stays, unanswered, for a retry). The model is not told of what went.
    /// </summary>
    public override void DeleteFrom(int index)
    {
        if ((uint)index >= (uint)Entries.Count)
        {
            return;
        }

        Stop();
        if (TurnOf(index) is { } turn)
        {
            if (index <= turn.Entry)
            {
                Truncate(turn, keepUser: false);
            }
            else
            {
                Truncate(turn, keepUser: true);
                ReplaceFrom(index, []);
            }
        }
        else
        {
            ReplaceFrom(index, []);
        }

        Status = null;
        Touch();
        Save();
    }

    // The exchange whose entries hold `index`: the last one starting at or before it.
    private Turn? TurnOf(int index) => _turns.LastOrDefault(t => t.Entry >= 0 && t.Entry <= index);

    // Drops `turn` (keeping its user message unanswered when asked) and every later exchange, with their entries.
    private void Truncate(Turn turn, bool keepUser)
    {
        int at = _turns.IndexOf(turn);
        if (at < 0)
        {
            return;
        }

        _turns.RemoveRange(keepUser ? at + 1 : at, _turns.Count - (keepUser ? at + 1 : at));
        if (keepUser)
        {
            turn.Reply = null;
            ReplaceFrom(turn.Entry + 1, []);
        }
        else
        {
            ReplaceFrom(turn.Entry, []);
        }
    }

    // What goes back to the model: the answered exchanges, the last chatHistory messages.
    private List<ChatMessage> HistoryMessages()
    {
        var messages = new List<ChatMessage>(_turns.Count * 2);
        foreach (var turn in _turns)
        {
            if (turn.Reply is { Length: > 0 } reply)
            {
                messages.Add(turn.User);
                messages.Add(new ChatMessage("assistant", reply));
            }
        }

        int limit = Math.Max(0, _settings().ChatHistory);
        if (messages.Count > limit)
        {
            messages.RemoveRange(0, messages.Count - limit);
        }

        return messages;
    }

    private void Touch()
    {
        _currentTime = DateTimeOffset.Now;
        ConversationsChanged?.Invoke(this, EventArgs.Empty);
    }

    // The shown conversation into the list (when it has anything), so another can show.
    private void Capture()
    {
        if (_currentId is not null && (Entries.Count > 0 || _turns.Count > 0))
        {
            _others.Add(CaptureCurrent());
        }
    }

    private KeptConversation CaptureCurrent() => new()
    {
        Id = _currentId ?? NewId(), Title = _currentTitle, TitleSet = _titleSet, Time = _currentTime,
        Turns = [.. _turns.Select(t => new KeptTurn(t.Entry, t.User.Content, t.Reply))],
        Entries = [.. Entries.Select(KeptEntry.Of)],
        Model = _session.Settings,
    };

    private void StartEmpty()
    {
        _turns.Clear();
        ClearEntries();
        Status = null;
        _currentId = null;
        _currentTitle = "";
        _titleSet = false;
        _session.End();                                                  // a new conversation: the shared settings, the old copy let go
    }

    // Shows a kept conversation: its exchanges and entries (a code block's actions made again).
    private void Show(KeptConversation kept)
    {
        _turns.Clear();
        _turns.AddRange(kept.Turns.Select(t => new Turn(t.Entry, new ChatMessage("user", t.User)) { Reply = t.Reply }));
        _currentId = kept.Id;
        _currentTitle = kept.Title;
        _titleSet = kept.TitleSet;
        _currentTime = kept.Time;
        Status = null;
        _restoring = true;
        try
        {
            _session.Restore(kept.Model);
        }
        finally
        {
            _restoring = false;
        }

        ReplaceFrom(0, [.. kept.Entries.Select(e => e.Role == ConversationRole.Code ? CodeEntry(e.Text, e.Language) with { Metrics = e.Metrics } : e.ToEntry())]);
    }

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    private static string TitleOf(string message)
    {
        string line = message.ReplaceLineEndings(" ").Trim();
        return line.Length <= 60 ? line : string.Concat(line.AsSpan(0, 59), "…");
    }

    /// <inheritdoc />
    public override string Placeholder => "Ask about this file. @ adds context, / runs a code action.";

    /// <summary>The chat's model role (note 25): the composer's chip shows and writes the chat's model and device.</summary>
    public override string? ModelRoleKey => ModelRoleKeys.Chat;

    /// <inheritdoc />
    public override string? EmptyText =>
        "Ask about the open file or the selection: explain it, find a bug, write a test. @ adds a file to the context; /explain, /fix, /doc, /refactor and /tests run a code action, and every edit comes as a diff you accept.";

    /// <inheritdoc />
    public override IReadOnlyList<PanelAction> HeaderActions => IsBusy
        ? [new PanelAction("Stop", _ => { Stop(); return Task.CompletedTask; })]
        : [new PanelAction("New chat", _ => { NewChat(); return Task.CompletedTask; })];

    /// <inheritdoc />
    public override void RemoveChip(string id)
    {
        _removed.Add(id);
        _extra.RemoveAll(c => string.Equals(c.Id, id, StringComparison.Ordinal));
        _images.Remove(id);
        RefreshChips();
    }

    /// <summary>
    /// Files dropped on the chat (the tree's, or the system's file manager's): a text file becomes a <c>path:</c> chip (its
    /// text read when the message goes, off the UI thread), an image an <c>image:</c> chip (its bytes read now, off the UI
    /// thread, up to <see cref="AiSettings.ChatImageMaxMegabytes"/>), greyed with the reason when the chat's model takes no
    /// images; a folder, a missing file, another binary file or a larger image is not taken, said in the panel.
    /// </summary>
    public override async Task<int> AcceptDropAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return 0;
        }

        var settings = _settings();
        long most = settings.ChatImageMaxMegabytes * 1024L * 1024L;
        var read = await Task.Run(() => ReadDropped(paths, most, cancellationToken), cancellationToken).ConfigureAwait(false);
        var taken = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() => taken.TrySetResult(TakeDropped(read, settings)));
        return await taken.Task.ConfigureAwait(false);
    }

    /// <summary>A test's look at each file the chat reads (its path), called on the thread that reads it, before it reads.</summary>
    internal static Action<string>? Reading { get; set; }

    // The kinds of files a drop carries, read on the thread pool.
    private enum DroppedKind
    {
        Text,
        Image,
        Folder,
        Missing,
        Binary,
        TooLarge,
        Unreadable,
    }

    private sealed record Dropped(string Path, DroppedKind Kind, ChatImage? Image = null, long Length = 0);

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff" };

    // On the thread pool: what each dropped path is; an image's bytes are read here, a text file's only its first block (to
    // tell text from binary: a NUL byte), its text when the message goes.
    private static List<Dropped> ReadDropped(IReadOnlyList<string> paths, long most, CancellationToken cancellationToken)
    {
        var found = new List<Dropped>(paths.Count);
        foreach (var given in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.GetFullPath(given);
            try
            {
                if (Directory.Exists(path))
                {
                    found.Add(new Dropped(path, DroppedKind.Folder));
                    continue;
                }

                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    found.Add(new Dropped(path, DroppedKind.Missing));
                    continue;
                }

                if (ImageExtensions.Contains(Path.GetExtension(path)))
                {
                    if (info.Length > most || info.Length == 0)
                    {
                        found.Add(new Dropped(path, DroppedKind.TooLarge, Length: info.Length));
                        continue;
                    }

                    Reading?.Invoke(path);
                    found.Add(new Dropped(path, DroppedKind.Image, ChatImage.FromFile(path), info.Length));
                    continue;
                }

                Reading?.Invoke(path);
                found.Add(new Dropped(path, LooksBinary(path) ? DroppedKind.Binary : DroppedKind.Text, Length: info.Length));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            {
                found.Add(new Dropped(path, DroppedKind.Unreadable));
            }
        }

        return found;
    }

    // A NUL byte in the file's first block: not text.
    private static bool LooksBinary(string path)
    {
        Span<byte> head = stackalloc byte[512];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
        int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        return head[..read].Contains((byte)0);
    }

    // On the UI thread: the chips of what was read; what was not taken said in one note. The count taken.
    private int TakeDropped(List<Dropped> dropped, AiSettings settings)
    {
        int taken = 0;
        var refused = new List<string>();
        foreach (var item in dropped)
        {
            string name = Path.GetFileName(item.Path);
            switch (item.Kind)
            {
                case DroppedKind.Text:
                    string id = "path:" + item.Path;
                    if (!_extra.Any(c => c.Id == id))
                    {
                        _extra.Add(new ContextChip(id, Shown(item.Path)));
                    }

                    taken++;
                    break;
                case DroppedKind.Image:
                    string imageId = "image:" + item.Path;
                    _extra.RemoveAll(c => c.Id == imageId);
                    _extra.Add(new ContextChip(imageId, "image: " + name) { Problem = ImageProblem(settings) });
                    _images[imageId] = item.Image!;
                    taken++;
                    break;
                case DroppedKind.TooLarge:
                    refused.Add(string.Create(CultureInfo.InvariantCulture, $"{name} is {item.Length / (1024.0 * 1024.0):0.#} MB, over chatImageMaxMb ({settings.ChatImageMaxMegabytes} MB)"));
                    break;
                case DroppedKind.Folder:
                    refused.Add($"{name} is a folder (drop its files)");
                    break;
                case DroppedKind.Binary:
                    refused.Add($"{name} is neither a text file nor an image");
                    break;
                case DroppedKind.Missing:
                    refused.Add($"{name} is not there");
                    break;
                default:
                    refused.Add($"{name} could not be read");
                    break;
            }
        }

        if (refused.Count > 0)
        {
            Add(new ConversationEntry(ConversationRole.Note, "Not added: " + string.Join("; ", refused) + "."));
        }

        RefreshChips();
        return taken;
    }

    // Why an image chip cannot go now: the chat's model, as its last reply found it, takes no images (else null: it is
    // checked again when the message goes, once the model is loaded).
    private string? ImageProblem(AiSettings settings) =>
        _partKinds is { } kinds && !kinds.Contains(ChatParts.Image) && string.Equals(_partKindsModel, settings.ChatRole.Model, StringComparison.Ordinal)
            ? $"{_partKindsModel} takes no images: remove this chip to send."
            : null;

    private string Shown(string path) => Host.WorkspaceFolder is { } root && path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        ? Path.GetRelativePath(root, path)
        : Path.GetFileName(path);

    /// <inheritdoc />
    public override async Task SubmitAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        string message = text.Trim();
        if (message.Length == 0 || IsBusy)
        {
            return;
        }

        if (!_settings().ChatEnabled)
        {
            // Note 25: turned off, the chat loads nothing and says so, with a way to its settings.
            Add(new ConversationEntry(ConversationRole.Error, AiModels.Off(ModelRoleKeys.Chat)) { Actions = [SettingsAction(Host, SettingSections.Chat, "Open the chat's settings")] });
            return;
        }

        if (message[0] == '/')
        {
            string name = message[1..].Split(' ', 2)[0].ToLowerInvariant();
            if (name == "clear")
            {
                NewConversation();
                return;
            }

            if (RunAction is not { } run || !await run(name, cancellationToken).ConfigureAwait(true))
            {
                Add(new ConversationEntry(ConversationRole.Error, $"/{name} is not a code action here. Try /explain, /fix, /doc, /refactor or /tests (with a selection, or the caret on a problem), or /clear."));
            }

            return;
        }

        foreach (var word in message.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length > 1 && word[0] == '@')
            {
                AddContext(word[1..].TrimEnd(',', '.', ';', ':', '?', '!'));
            }
        }

        LastReply = AskAsync(message, cancellationToken);
    }

    /// <summary>
    /// Sends <paramref name="message"/> with the context of the chips (and <paramref name="context"/> when given) and
    /// streams the reply; completes when the reply ended, was stopped or failed (said so in the panel).
    /// </summary>
    public Task AskAsync(string message, CancellationToken cancellationToken = default, string? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (IsBusy)
        {
            Stop();
        }

        if (Chips.FirstOrDefault(c => c.Problem is not null) is { } greyed)
        {
            // A chip that cannot go (an image for a model that takes none): nothing is sent until it is removed.
            Add(new ConversationEntry(ConversationRole.Error, $"Not sent: {greyed.Text}: {greyed.Problem}"));
            return Task.CompletedTask;
        }

        var settings = _settings();
        // The context is gathered here (the active document's text, the open files' snapshots, the images read at the
        // drop) and the files that are not open are read on the thread pool when the message goes, never on the UI thread.
        var gathered = BuildContext();
        var history = HistoryMessages();
        var turn = new Turn(Entries.Count, new ChatMessage("user", message));
        Add(new ConversationEntry(ConversationRole.User, message));
        _turns.Add(turn);
        if (_currentId is null)
        {
            _currentId = NewId();
            _currentTitle = TitleOf(message);
            _titleSet = false;
        }

        Touch();
        var messages = new List<ChatMessage>(history.Count + 2) { new("system", SystemPrompt) };
        messages.AddRange(history);
        var asked = new Asked(message, context, gathered);
        _removed.Clear();
        _extra.Clear();
        _images.Clear();
        RefreshChips();
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = source;
        int generation = ++_generation;
        _segment.Clear();
        _reply.Clear();
        _segmentIndex = -1;
        _replyStart = Entries.Count;
        _toolRows.Clear();
        IsBusy = true;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Phase U: the reply shows in the status bar (tokens, tok/s) with Cancel, which stops it as Stop does.
        var activity = Host.Activities.Start("Chat: " + Cut(message, 48), ActivityKinds.Generation, cancellable: true, source.Token);
        _ = Task.Run(() => StreamAsync(messages, asked, turn, settings, generation, activity, done), CancellationToken.None);
        LastReply = done.Task;
        return done.Task;
    }

    /// <summary>A button that opens the settings page at <paramref name="section"/> (a role turned off links to its settings).</summary>
    internal static PanelAction SettingsAction(IEditorHost host, string section, string label) =>
        new(label, token => host.ExecuteCommandAsync(SettingSections.CommandOf(section), token), Primary: true);

    /// <summary>Stops the reply that streams (its text so far stays).</summary>
    public void Stop() => _running?.Cancel();

    /// <summary>Starts a new conversation (the header's New chat); the one shown stays in the list of past conversations.</summary>
    public void NewChat() => NewConversation();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _session.End();
        Host.ActiveDocumentChanged -= OnContextChanged;
        Host.SelectionChanged -= OnContextChanged;
    }

    /// <summary>A code block's entry: the code, "Apply as diff" and Copy.</summary>
    public ConversationEntry CodeEntry(string code, string? language) => new(ConversationRole.Code, code)
    {
        Language = language,
        CopyText = code,
        Actions = [new PanelAction("Apply as diff", token => ApplyAsync(code, token), Primary: true)],
    };

    /// <summary>
    /// Proposes <paramref name="code"/> to the active document as a diff (placed by <see cref="SnippetPlacement"/>); true when
    /// the user accepted it. A note in the panel says what happened.
    /// </summary>
    public async Task<bool> ApplyAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (Host.ActiveDocument is not { } document)
        {
            Add(new ConversationEntry(ConversationRole.Error, "No document is active: open the one to apply the code to."));
            return false;
        }

        var settings = _settings();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var snapshot = document.Document.Buffer.Current;
            var selection = document.Selection;
            (TextEdit Edit, string How) placed;
            try
            {
                placed = await Task.Run(() => SnippetPlacement.Place(snapshot, selection, code, settings), cancellationToken).ConfigureAwait(true);
            }
            catch (NotRegisteredException error)
            {
                Add(new ConversationEntry(ConversationRole.Error, error.Message));
                return false;
            }

            if (!ReferenceEquals(document.Document.Buffer.Current, snapshot))
            {
                continue;   // typed meanwhile: place it again on the new text
            }

            var proposal = new ProposedEdits([placed.Edit], "Apply the chat's code") { Placement = placed.How, Origin = $"Chat · {_modelName}" };
            var answer = AiEvents.Listening ? AnswerOf(AiAnswerKinds.Chat, _lastPrompt, _modelName, document, snapshot, placed.Edit, Host.WorkspaceFolder) : null;
            if (answer is not null)
            {
                AiEvents.Answered(answer);
            }

            bool applied = await Host.ProposeAsync(document, proposal, cancellationToken).ConfigureAwait(true);
            if (answer is not null)
            {
                AiEvents.Judged(VerdictOf(answer, applied, document, placed.Edit));
            }

            string name = document.FilePath is { } path ? Path.GetFileName(path) : "the document";
            Add(new ConversationEntry(ConversationRole.Note, applied ? $"Applied to {name} (one undo step)." : $"Not applied to {name}."));
            return applied;
        }

        Add(new ConversationEntry(ConversationRole.Error, "The document kept changing; apply it again."));
        return false;
    }

    /// <summary>The AI events' answer for an edit the plug-in proposes (<see cref="AiEvents"/>: the learning plug-in records it under the user's consent).</summary>
    internal static AiAnswer AnswerOf(string kind, string prompt, string model, IEditorDocument document, ITextSnapshot snapshot, TextEdit edit, string? workspace) =>
        new(AiEvents.NewId(), kind, prompt, edit.Text)
        {
            Plugin = AiSettings.PluginName,
            Model = model.Length == 0 ? null : model,
            Snapshot = snapshot,
            Position = edit.Start,
            Before = snapshot.GetText(edit.Start, edit.Length),
            FilePath = document.FilePath,
            LanguageName = document.LanguageName,
            WorkspaceFolder = workspace,
        };

    /// <summary>What the user did with a proposed edit: accepted (where it now lies) or rejected (the text kept instead).</summary>
    internal static AiVerdict VerdictOf(AiAnswer answer, bool accepted, IEditorDocument document, TextEdit edit) => accepted
        ? new AiVerdict(answer.Id, AiVerdicts.Accepted) { Document = document, Start = edit.Start, Length = edit.Text.Length }
        : new AiVerdict(answer.Id, AiVerdicts.Rejected) { Document = document, Kept = answer.Before is { Length: > 0 } before ? before : null };

    private async Task StreamAsync(List<ChatMessage> messages, Asked asked, Turn turn, AiSettings settings, int generation, IActivity activity, TaskCompletionSource done)
    {
        int tokens = 0;
        var watch = new Stopwatch();
        var token = activity.CancellationToken;
        var user = new ChatMessage("user", asked.Message);
        try
        {
            // On the thread pool: the files the chips name are read now (the fix found in X part 5: they were read on the UI thread).
            string withContext = asked.Context.Compose(settings, token) + (asked.Extra is null ? "" : asked.Extra + "\n\n") + asked.Message;
            user = asked.Context.Images.Count == 0
                ? new ChatMessage("user", withContext)
                : new ChatMessage("user", [.. asked.Context.Images.Select(i => i.Image), new ChatText(withContext)]);
            messages.Add(user);
            Post(() => _lastPrompt = withContext);
            activity.Report(null, "waiting for the model");
            if (_session.OwnLease ? !_models.IsSessionLoaded(ModelRoleKeys.Chat, _session.Settings) : !_models.IsChatLoaded)
            {
                // Loaded on the first message (never at start-up): the composer says so; the message goes once it is ready.
                Post(() => Status = $"Loading {settings.Model}… the message is sent once it is loaded (its progress and Cancel are in the status bar).");
            }

            var lease = await _session.LeaseAsync(token).ConfigureAwait(false);
            Post(() =>
            {
                if (Status is { } shown && shown.StartsWith("Loading ", StringComparison.Ordinal))
                {
                    Status = null;
                }
            });
            var chat = lease.Chat ?? throw new NotSupportedException($"The model {lease.Model} has no chat template, so it cannot chat.");
            var kinds = chat.PartKinds;
            string header = HeaderOf(lease.Model, settings.ModelProvider, ModelRoleKeys.Chat);
            Post(() =>
            {
                _modelName = lease.Model;
                _header = header;
                Subtitle = $"{lease.Model} · {settings.ModelProvider}";
                _partKinds = kinds;
                _partKindsModel = settings.ChatRole.Model;
            });
            if (asked.Context.Images.Count > 0 && !kinds.Contains(ChatParts.Image))
            {
                // The model takes no images: the image chips come back greyed with the reason, and nothing is sent.
                string why = $"{settings.ChatRole.Model} takes no images: remove this chip to send.";
                Post(() =>
                {
                    foreach (var image in asked.Context.Images)
                    {
                        _extra.Add(image.Chip with { Problem = why });
                        _images[image.Chip.Id] = image.Image;
                    }

                    RefreshChips();
                    Finish(generation, turn, user, null, $"Not sent: {lease.Model} takes no images. Remove the greyed image chip, then send again.");
                });
                activity.Complete("not sent: the model takes no images");
                return;
            }

            var observed = new ObservedTools(_tools, call => Post(() => ToolStarted(generation, call)), result => Post(() => ToolEnded(generation, result)));
            var model = chat.WithTools(observed, settings.ChatToolRounds);
            var options = _session.Sampling(lease.ContextLength, settings.ChatStops);                // the conversation's own sampling over the chat's
            int lastGenerated = 0;
            string? reason = null;
            watch.Start();
            await foreach (var chunk in model.StreamAsync(new ChatRequest(messages, Options: options), token).ConfigureAwait(false))
            {
                int now = chunk.Generated;
                tokens += now > lastGenerated ? now - lastGenerated : now < lastGenerated ? now : chunk.Delta.Content.Length > 0 ? 1 : 0;
                lastGenerated = now;
                if (chunk.Done)
                {
                    reason = chunk.DoneReason;
                }

                string piece = chunk.Delta.Content;
                if (piece.Length > 0)
                {
                    Queue(generation, piece);
                }

                double so = watch.Elapsed.TotalSeconds;
                activity.Report(null, string.Create(CultureInfo.InvariantCulture, $"{tokens:N0} tokens"), so > 0 ? new ActivityRate(tokens / so, "tok/s") : null);
            }

            double seconds = watch.Elapsed.TotalSeconds;
            // A reply cut by its limit is said so (never a silent half sentence): the setting, or the context window's room.
            string? cut = reason == "length"
                ? string.Create(CultureInfo.InvariantCulture, $"The reply reached its limit ({tokens} tokens: chatMaxTokens {settings.ChatMaxTokens}, or the room the model's context of {lease.ContextLength} left after the prompt).")
                : null;
            Post(() => Finish(generation, turn, user, Rate(tokens, seconds), cut, Metrics(tokens, seconds)));
            activity.Complete(Rate(tokens, seconds));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Post(() => Finish(generation, turn, user, Rate(tokens, watch.Elapsed.TotalSeconds), "Stopped.", Metrics(tokens, watch.Elapsed.TotalSeconds)));
            activity.Dispose();
        }
#pragma warning disable CA1031 // The failure is shown in the panel; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            string said = AiErrors.Describe(error, settings);
            Post(() =>
            {
                Finish(generation, turn, user, null, said);
                Host.Log(EditorLogLevel.Error, "The chat reply failed.", error);       // the log is the UI thread's
            });
            activity.Fail("the reply failed: " + said, error);
        }
        finally
        {
            Post(() => done.TrySetResult());
        }
    }

    // Any thread: the pieces since the last UI turn go in one dispatch (never one per token, which redrew the reply per token).
    private void Queue(int generation, string piece)
    {
        bool schedule;
        lock (_incomingGate)
        {
            if (generation != _incomingGeneration)
            {
                _incoming.Clear();                 // an older reply's pieces never reach this one
                _incomingGeneration = generation;
            }

            _incoming.Append(piece);
            schedule = !_incomingPending;
            _incomingPending = true;
        }

        if (schedule)
        {
            Post(FlushIncoming);
        }
    }

    // On the UI thread: the pieces queued so far, in order (a tool row or the end flushes first, so the text stays in order).
    private void FlushIncoming()
    {
        string pieces;
        int generation;
        lock (_incomingGate)
        {
            pieces = _incoming.ToString();
            generation = _incomingGeneration;
            _incoming.Clear();
            _incomingPending = false;
        }

        if (pieces.Length > 0)
        {
            Append(generation, pieces);
        }
    }

    private static string Cut(string text, int width)
    {
        string line = text.ReplaceLineEndings(" ");
        return line.Length <= width ? line : string.Concat(line.AsSpan(0, width - 1), "…");
    }

    private void Append(int generation, string piece)
    {
        if (generation != _generation)
        {
            return;
        }

        _segment.Append(piece);
        _reply.Append(piece);
        var entry = new ConversationEntry(ConversationRole.Assistant, _segment.ToString()) { IsStreaming = true, Header = _header };
        if (_segmentIndex < 0)
        {
            _segmentIndex = Add(entry);
        }
        else
        {
            Update(_segmentIndex, entry);
        }
    }

    private void ToolStarted(int generation, ToolCall call)
    {
        FlushIncoming();
        if (generation != _generation)
        {
            return;
        }

        EndSegment();
        _segment.Clear();
        _segmentIndex = -1;
        _reply.Append('\n');
        _toolRows[call] = Add(new ConversationEntry(ConversationRole.Tool, call.Name) { Detail = "running…", Details = DetailsOf(call, null) });
    }

    private void ToolEnded(int generation, ToolResult result)
    {
        FlushIncoming();
        if (generation != _generation || !_toolRows.TryGetValue(result.Call, out int index))
        {
            return;
        }

        Update(index, new ConversationEntry(ConversationRole.Tool, result.Call.Name) { Detail = "→ " + Summarize(result), Succeeded = result.Succeeded, Details = DetailsOf(result.Call, result) });
    }

    // The streamed segment stops growing (a tool call starts after it): its caret goes.
    private void EndSegment()
    {
        if (_segmentIndex >= 0 && _segmentIndex < Entries.Count && Entries[_segmentIndex].IsStreaming)
        {
            Update(_segmentIndex, Entries[_segmentIndex] with { IsStreaming = false });
        }
    }

    private void Finish(int generation, Turn turn, ChatMessage user, string? status, string? problem, string? metrics = null)
    {
        FlushIncoming();
        if (generation != _generation)
        {
            return;
        }

        _running?.Dispose();
        _running = null;
        // The reply's text entries split at their code blocks (each with Apply as diff and Copy); tool rows stay.
        var rebuilt = new List<ConversationEntry>();
        for (int i = _replyStart; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            if (entry.Role == ConversationRole.Assistant)
            {
                rebuilt.AddRange(Split(entry.Text, CodeEntry).Select(e => e.Role == ConversationRole.Assistant ? e with { Header = _header } : e));
            }
            else
            {
                rebuilt.Add(entry);
            }
        }

        if (problem is not null)
        {
            bool note = problem == "Stopped." || problem.StartsWith("The reply reached its limit", StringComparison.Ordinal);
            rebuilt.Add(new ConversationEntry(note ? ConversationRole.Note : ConversationRole.Error, problem));
        }

        // The reply's rate and time small under it (CRAFT_CHAT_SHOW_METRICS): on its last text or code.
        int last = rebuilt.FindLastIndex(e => e.Role is ConversationRole.Assistant or ConversationRole.Code);
        if (metrics is not null && last >= 0)
        {
            rebuilt[last] = rebuilt[last] with { Metrics = metrics };
        }
        else if (status is not null)
        {
            rebuilt.Add(new ConversationEntry(ConversationRole.Note, status));
        }

        ReplaceFrom(_replyStart, rebuilt);
        Status = status;
        string reply = _reply.ToString().Trim();
        turn.User = user;
        turn.Reply = reply.Length > 0 ? reply : null;
        Touch();

        IsBusy = false;
        Save();
    }

    // A message as asked, and its context gathered on the UI thread (the files not open read later, off it).
    private sealed record Asked(string Message, string? Extra, ChatContext Context);

    private sealed record ContextImage(ContextChip Chip, ChatImage Image);

    // A chip's file: the open document's snapshot (immutable, read on any thread) or its path (read off the UI thread).
    private sealed record ContextFile(string Shown, string Path, ITextSnapshot? Open);

    private sealed record ChatContext(string Head, IReadOnlyList<ContextFile> Files, IReadOnlyList<ContextImage> Images)
    {
        // On the thread pool: the head, then each file's first chatFileLines lines (a file not open read from the disk).
        public string Compose(AiSettings settings, CancellationToken cancellationToken)
        {
            var text = new StringBuilder(Head);
            foreach (var file in Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? content = file.Open is { } open ? Lines(open, settings.ChatFileLines) : Read(file.Path, settings.ChatFileLines);
                if (content is not null)
                {
                    text.Append(CultureInfo.InvariantCulture, $"The file {file.Shown}:\n```\n").Append(content).Append("\n```\n");
                }
            }

            return text.Length == 0 ? "" : text.Append('\n').ToString();
        }

        private static string Lines(ITextSnapshot snapshot, int most)
        {
            int last = Math.Min(snapshot.LineCount, most) - 1;
            if (last < 0)
            {
                return "";
            }

            int end = snapshot.GetLine(last).End;
            return snapshot.GetText(0, end);
        }

        private static string? Read(string path, int most)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                Reading?.Invoke(path);
                return string.Join('\n', File.ReadLines(path).Take(most));      // only the lines sent are read
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    private ChatContext BuildContext()
    {
        var text = new StringBuilder();
        var chips = Chips;
        if (chips.Count > 0 && Host.ActiveDocument is { } document)
        {
            ActiveContext(text, chips, document);
        }

        var files = new List<ContextFile>();
        var images = new List<ContextImage>();
        foreach (var chip in _extra)
        {
            if (chip.Id.StartsWith("image:", StringComparison.Ordinal))
            {
                if (_images.TryGetValue(chip.Id, out var image))
                {
                    images.Add(new ContextImage(chip, image));
                }

                continue;
            }

            string path = chip.Id["path:".Length..];
            var open = Host.Documents.FirstOrDefault(d => d.FilePath is { } p && string.Equals(Path.GetFullPath(p), path, StringComparison.OrdinalIgnoreCase));
            files.Add(new ContextFile(chip.Text, path, open?.Document.Buffer.Current));
        }

        return new ChatContext(text.ToString(), files, images);
    }

    private void ActiveContext(StringBuilder text, IReadOnlyList<ContextChip> chips, IEditorDocument document)
    {
        var snapshot = document.Document.Buffer.Current;
        string name = DisplayName(document);
        if (chips.Any(c => c.Id == "file"))
        {
            int caretLine = snapshot.GetLineIndex(Math.Clamp(document.Selection.Caret, 0, snapshot.Length));
            int first = Math.Max(0, caretLine - 40);
            int last = Math.Min(snapshot.LineCount - 1, caretLine + 40);
            int start = snapshot.GetLine(first).Start;
            int end = snapshot.GetLine(last).EndIncludingBreak;
            text.Append(CultureInfo.InvariantCulture, $"The active document is {name} ({document.LanguageName}, {snapshot.LineCount} lines; read_document reads the rest). ")
                .Append(CultureInfo.InvariantCulture, $"Lines {first + 1}-{last + 1} around the caret (line {caretLine + 1}):\n```\n")
                .Append(snapshot.GetText(start, end - start)).Append("\n```\n");
        }

        var selection = document.Selection;
        if (chips.Any(c => c.Id == "selection") && !selection.IsEmpty)
        {
            int start = Math.Clamp(selection.Start, 0, snapshot.Length);
            int end = Math.Clamp(selection.End, start, snapshot.Length);
            text.Append(CultureInfo.InvariantCulture, $"The selection (lines {snapshot.GetLineIndex(start) + 1}-{snapshot.GetLineIndex(end) + 1}):\n```\n")
                .Append(snapshot.GetText(start, end - start)).Append("\n```\n");
        }

        if (chips.Any(c => c.Id == "problems"))
        {
            var set = document.Diagnostics;
            var at = set.Snapshot ?? snapshot;
            text.Append("Its problems:\n");
            int listed = 0;
            foreach (var item in set.Items)
            {
                if (item.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning) || ++listed > 50)
                {
                    continue;
                }

                int line = at.GetLineIndex(Math.Clamp(item.Start, 0, at.Length)) + 1;
                text.Append(CultureInfo.InvariantCulture, $"{name}:{line}: {item.Severity.ToString().ToLowerInvariant()} {item.Id}: {item.Message}\n");
            }
        }
    }

    private void AddContext(string what)
    {
        switch (what.ToLowerInvariant())
        {
            case "selection":
            case "problems":
            case "file":
                _removed.Remove(what.ToLowerInvariant());
                break;
            default:
                string? full = null;
                if (Path.IsPathRooted(what))
                {
                    full = what;
                }
                else if (Host.WorkspaceFolder is { } root)
                {
                    full = Path.Combine(root, what);
                }

                if (full is not null && (File.Exists(full) || Host.Documents.Any(d => d.FilePath is { } p && string.Equals(Path.GetFullPath(p), Path.GetFullPath(full), StringComparison.OrdinalIgnoreCase))))
                {
                    full = Path.GetFullPath(full);
                    if (!_extra.Any(c => c.Id == "path:" + full))
                    {
                        _extra.Add(new ContextChip("path:" + full, Path.GetFileName(full)));
                    }
                }

                break;
        }

        RefreshChips();
    }

    private void OnContextChanged(object? sender, EventArgs e)
    {
        LoadIfNeeded();
        RefreshChips();
    }

    private void RefreshChips()
    {
        var chips = new List<ContextChip>();
        if (Host.ActiveDocument is { } document)
        {
            if (!_removed.Contains("file"))
            {
                chips.Add(new ContextChip("file", DisplayName(document)));
            }

            var selection = document.Selection;
            if (!selection.IsEmpty && !_removed.Contains("selection"))
            {
                var snapshot = document.Document.Buffer.Current;
                int first = snapshot.GetLineIndex(Math.Clamp(selection.Start, 0, snapshot.Length)) + 1;
                int last = snapshot.GetLineIndex(Math.Clamp(selection.End, 0, snapshot.Length)) + 1;
                chips.Add(new ContextChip("selection", first == last ? $"Line {first}" : $"Lines {first}–{last}"));
            }

            var set = document.Diagnostics;
            if (set.Errors + set.Warnings > 0 && !_removed.Contains("problems"))
            {
                chips.Add(new ContextChip("problems", Problems(set.Errors, set.Warnings)));
            }
        }

        chips.AddRange(_extra);
        if (!chips.SequenceEqual(Chips))
        {
            SetChips(chips);
        }
    }

    private static string Problems(int errors, int warnings)
    {
        var parts = new List<string>(2);
        if (errors > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{errors} {(errors == 1 ? "error" : "errors")}"));
        }

        if (warnings > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{warnings} {(warnings == 1 ? "warning" : "warnings")}"));
        }

        return string.Join(", ", parts);
    }

    private string DisplayName(IEditorDocument document) => document.FilePath is { } path
        ? Host.WorkspaceFolder is { } root && Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase) ? Path.GetRelativePath(root, path) : Path.GetFileName(path)
        : "untitled";

    private string? ChatPath()
    {
        var settings = _settings();
        return settings.ChatKeep && Host.WorkspaceFolder is { } root ? Path.Combine(root, settings.ChatFile) : null;
    }

    /// <summary>
    /// Completes when the kept chat of the current folder has been read and shown (or there was none): it is read and
    /// parsed on the thread pool, never on the UI thread (the owner's start-up stalls), and shown through the dispatcher.
    /// </summary>
    public Task KeptRead { get; private set; } = Task.CompletedTask;

    private void LoadIfNeeded()
    {
        if (string.Equals(_loadedFolder, Host.WorkspaceFolder, StringComparison.Ordinal))
        {
            return;
        }

        _loadedFolder = Host.WorkspaceFolder;
        _turns.Clear();
        _others.Clear();
        _deleted.Clear();
        _currentId = null;
        _currentTitle = "";
        _session?.End();                                                 // another workspace: the conversation's own copy goes
        ReplaceFrom(0, []);
        int generation = ++_keptGeneration;
        if (ChatPath() is not { } path)
        {
            KeptRead = Task.CompletedTask;
            return;
        }

        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        KeptRead = shown.Task;
        _ = ShowKeptAsync(path, generation, shown);
    }

    // The kept conversations read and parsed on the thread pool, then shown on the UI thread (only if nothing was written meanwhile).
    private async Task ShowKeptAsync(string path, int generation, TaskCompletionSource shown)
    {
        KeptFile? kept = null;
        Exception? failure = null;
        try
        {
            kept = await Task.Run(() => ReadKept(path)).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A kept chat that cannot be read is logged; the chat starts empty.
        catch (Exception error)
#pragma warning restore CA1031
        {
            failure = error;
        }

        Post(() =>
        {
            try
            {
                // Another folder meanwhile, or the user already wrote: what was read is not shown over it.
                if (_disposed || generation != _keptGeneration || _turns.Count > 0 || Entries.Count > 0)
                {
                    return;
                }

                if (kept is not null)
                {
                    _others.AddRange(kept.Conversations);
                    if ((_others.FirstOrDefault(c => c.Id == kept.Current) ?? _others.OrderByDescending(c => c.Time).FirstOrDefault()) is { } current)
                    {
                        _others.Remove(current);
                        Show(current);
                    }

                    ConversationsChanged?.Invoke(this, EventArgs.Empty);
                }
                else if (failure is not null)
                {
                    Host.Log(EditorLogLevel.Warning, "The kept chat could not be read; it starts empty.", failure);
                }
            }
            finally
            {
                shown.TrySetResult();
            }
        });
    }

    // An exchange: the index of the user's entry, what was sent (with its context), the reply (null: not answered).
    private sealed class Turn(int entry, ChatMessage user)
    {
        public int Entry { get; } = entry;

        public ChatMessage User { get; set; } = user;

        public string? Reply { get; set; }
    }

    private sealed record KeptTurn(int Entry, string User, string? Reply);

    private sealed record KeptEntry(ConversationRole Role, string Text, string? Detail, string? Language, string? Header, string? Metrics)
    {
        public static KeptEntry Of(ConversationEntry entry) => new(entry.Role, entry.Text, entry.Detail, entry.Language, entry.Header, entry.Metrics);

        public ConversationEntry ToEntry() => new(Role, Text) { Detail = Detail, Language = Language, Header = Header, Metrics = Metrics };
    }

    private sealed class KeptConversation
    {
        public string Id { get; init; } = "";

        public string Title { get; set; } = "";

        public bool TitleSet { get; set; }

        public DateTimeOffset Time { get; init; }

        public List<KeptTurn> Turns { get; init; } = [];

        public List<KeptEntry> Entries { get; init; } = [];

        public ModelSessionSettings Model { get; init; } = ModelSessionSettings.None;
    }

    private sealed record KeptFile(string? Current, List<KeptConversation> Conversations);

    // On the thread pool: the kept file read and parsed (null when there is none); a broken one throws to be logged.
    // Version 1 (one conversation: its messages and entries) is read as one conversation.
    private static KeptFile? ReadKept(string path)
    {
        if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject kept)
        {
            return null;
        }

        if (kept["conversations"] is JsonArray list)
        {
            var conversations = new List<KeptConversation>();
            foreach (var node in list)
            {
                if (node is JsonObject conversation)
                {
                    conversations.Add(ReadConversation(conversation));
                }
            }

            return new KeptFile(kept["current"]?.GetValue<string>(), conversations);
        }

        var one = ReadConversation(kept);
        if (one.Entries.Count == 0)
        {
            return null;
        }

        // Version 1: the user's and the model's messages in pairs, the k-th exchange at the k-th user entry.
        var messages = kept["messages"] as JsonArray ?? [];
        var users = one.Entries.Select((e, i) => (e, i)).Where(x => x.e.Role == ConversationRole.User).Select(x => x.i).ToList();
        int k = 0;
        for (int i = 0; i + 1 < messages.Count; i += 2)
        {
            if (messages[i]?["content"]?.GetValue<string>() is { } user && messages[i + 1]?["content"]?.GetValue<string>() is { } reply)
            {
                one.Turns.Add(new KeptTurn(k < users.Count ? users[k] : -1, user, reply));
                k++;
            }
        }

        string first = one.Entries.FirstOrDefault(e => e.Role == ConversationRole.User)?.Text ?? "Chat";
        return new KeptFile(null, [new KeptConversation { Id = NewId(), Title = TitleOf(first), Time = File.GetLastWriteTimeUtc(path), Turns = one.Turns, Entries = one.Entries }]);
    }

    private static KeptConversation ReadConversation(JsonObject node)
    {
        var entries = new List<KeptEntry>();
        foreach (var item in node["entries"] as JsonArray ?? [])
        {
            if (item?["role"]?.GetValue<string>() is { } role && Enum.TryParse<ConversationRole>(role, out var kind) && item["text"]?.GetValue<string>() is { } text)
            {
                entries.Add(new KeptEntry(kind, text, item["detail"]?.GetValue<string>(), item["language"]?.GetValue<string>(), item["header"]?.GetValue<string>(), item["metrics"]?.GetValue<string>()));
            }
        }

        var turns = new List<KeptTurn>();
        foreach (var item in node["turns"] as JsonArray ?? [])
        {
            if (item?["user"]?.GetValue<string>() is { } user)
            {
                turns.Add(new KeptTurn(item["entry"]?.GetValue<int>() ?? -1, user, item["reply"]?.GetValue<string>()));
            }
        }

        DateTimeOffset time = node["time"]?.GetValue<string>() is { } stamp && DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTimeOffset.UnixEpoch;
        return new KeptConversation
        {
            Id = node["id"]?.GetValue<string>() ?? NewId(), Title = node["title"]?.GetValue<string>() ?? "", TitleSet = node["titleSet"]?.GetValue<bool>() ?? false,
            Time = time, Turns = turns, Entries = entries, Model = SessionModel.Read(node["model"]),
        };
    }

    private static JsonObject WriteConversation(KeptConversation conversation)
    {
        var turns = new JsonArray();
        foreach (var turn in conversation.Turns)
        {
            turns.Add((JsonNode)new JsonObject { ["entry"] = turn.Entry, ["user"] = turn.User, ["reply"] = turn.Reply });
        }

        var entries = new JsonArray();
        foreach (var entry in conversation.Entries)
        {
            entries.Add((JsonNode)new JsonObject
            {
                ["role"] = entry.Role.ToString(), ["text"] = entry.Text, ["detail"] = entry.Detail, ["language"] = entry.Language, ["header"] = entry.Header, ["metrics"] = entry.Metrics,
            });
        }

        return new JsonObject
        {
            ["id"] = conversation.Id, ["title"] = conversation.Title, ["titleSet"] = conversation.TitleSet,
            ["time"] = conversation.Time.ToString("O", CultureInfo.InvariantCulture), ["turns"] = turns, ["entries"] = entries,
            ["model"] = SessionModel.Write(conversation.Model),
        };
    }

    private void Save()
    {
        if (ChatPath() is not { } path)
        {
            return;
        }

        try
        {
            bool hasCurrent = _currentId is not null && (_turns.Count > 0 || Entries.Count > 0);
            if (!hasCurrent && _others.Count == 0)
            {
                _writes = _writes.ContinueWith(_ =>
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        // A file that cannot go now is read back next time: the conversation shows again, nothing is lost.
                    }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                return;
            }

            var conversations = new JsonArray();
            if (hasCurrent)
            {
                conversations.Add((JsonNode)WriteConversation(CaptureCurrent()));
            }

            foreach (var other in _others)
            {
                conversations.Add((JsonNode)WriteConversation(other));
            }

            // Serialized here (the entries are the UI thread's), written on the thread pool after the writes before it.
            string json = new JsonObject { ["version"] = 2, ["current"] = hasCurrent ? _currentId : null, ["conversations"] = conversations }.ToJsonString();
            _writes = _writes.ContinueWith(_ =>
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, json);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    Post(() => Host.Log(EditorLogLevel.Warning, $"The chat could not be kept in {path}.", error));
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Host.Log(EditorLogLevel.Warning, $"The chat could not be kept in {path}.", error);
        }
    }

    /// <summary>Completes when the conversation's file holds every change so far (the tests await it).</summary>
    public Task Kept => _writes;
}
