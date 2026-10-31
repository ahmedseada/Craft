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
public sealed class AiChatPanel : ConversationPanel, IDisposable
{
    /// <summary>The panel's id.</summary>
    public const string PanelId = "ai.chat";

    private const string SystemPrompt = "You are a coding assistant inside Craft, a code editor. Answer briefly and exactly. "
                                        + "Use the editor's tools to read documents, the selection and the diagnostics when you need them. "
                                        + "Put code the user may apply in one fenced block per change, with only the code that replaces or is added.";

    private readonly Func<AiSettings> _settings;
    private readonly AiModels _models;
    private readonly IToolRegistry _tools;
    private readonly List<ChatMessage> _history = [];
    private readonly HashSet<string> _removed = new(StringComparer.Ordinal);
    private readonly List<ContextChip> _extra = [];
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

    /// <summary>A chat panel in <paramref name="host"/>.</summary>
    public AiChatPanel(IEditorHost host, Func<AiSettings> settings, AiModels models)
        : base(host, PanelId, "Chat")
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _models = models ?? throw new ArgumentNullException(nameof(models));
        _tools = ToolRegistry.Create().Add(EditorToolSets.Live(host)).Build();   // every registered tool set (the tools plug-in's "editor", and any plug-in's)
        host.ActiveDocumentChanged += OnContextChanged;
        host.SelectionChanged += OnContextChanged;
        LoadIfNeeded();
        RefreshChips();
    }

    /// <summary>Runs a slash command's code action ("explain", "fix", "doc", "refactor", "tests"); set by the session; true when one ran.</summary>
    public Func<string, CancellationToken, Task<bool>>? RunAction { get; set; }

    /// <summary>The reply streaming now, or the last one (the tests await it); completes when it ended, was stopped or failed.</summary>
    public Task LastReply { get; private set; } = Task.CompletedTask;

    /// <summary>The messages kept (user and assistant), oldest first.</summary>
    public IReadOnlyList<ChatMessage> History => _history;

    /// <inheritdoc />
    public override string Placeholder => "Ask about this file. @ adds context, / runs a code action.";

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
        RefreshChips();
    }

    /// <inheritdoc />
    public override async Task SubmitAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        string message = text.Trim();
        if (message.Length == 0 || IsBusy)
        {
            return;
        }

        if (message[0] == '/')
        {
            string name = message[1..].Split(' ', 2)[0].ToLowerInvariant();
            if (RunAction is not { } run || !await run(name, cancellationToken).ConfigureAwait(true))
            {
                Add(new ConversationEntry(ConversationRole.Error, $"/{name} is not a code action here. Try /explain, /fix, /doc, /refactor or /tests (with a selection, or the caret on a problem)."));
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

        var settings = _settings();
        string withContext = BuildContext(settings) + (context is null ? "" : context + "\n\n") + message;
        Add(new ConversationEntry(ConversationRole.User, message));
        var messages = new List<ChatMessage>(_history.Count + 2) { new("system", SystemPrompt) };
        messages.AddRange(_history);
        var user = new ChatMessage("user", withContext);
        _lastPrompt = withContext;
        messages.Add(user);
        _removed.Clear();
        _extra.Clear();
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
        _ = Task.Run(() => StreamAsync(messages, user, settings, generation, activity, done), CancellationToken.None);
        LastReply = done.Task;
        return done.Task;
    }

    /// <summary>Stops the reply that streams (its text so far stays).</summary>
    public void Stop() => _running?.Cancel();

    /// <summary>Clears the conversation (and its kept file).</summary>
    public void NewChat()
    {
        Stop();
        _history.Clear();
        ClearEntries();
        Status = null;
        Save();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
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

    private async Task StreamAsync(List<ChatMessage> messages, ChatMessage user, AiSettings settings, int generation, IActivity activity, TaskCompletionSource done)
    {
        int tokens = 0;
        var watch = new Stopwatch();
        var token = activity.CancellationToken;
        try
        {
            activity.Report(null, "waiting for the model");
            if (!_models.IsChatLoaded)
            {
                // Loaded on the first message (never at start-up): the composer says so; the message goes once it is ready.
                Post(() => Status = $"Loading {settings.Model}… the message is sent once it is loaded (its progress and Cancel are in the status bar).");
            }

            var lease = await _models.ChatAsync(token).ConfigureAwait(false);
            Post(() =>
            {
                if (Status is { } shown && shown.StartsWith("Loading ", StringComparison.Ordinal))
                {
                    Status = null;
                }
            });
            var chat = lease.Chat ?? throw new NotSupportedException($"The model {lease.Model} has no chat template, so it cannot chat.");
            Post(() =>
            {
                _modelName = lease.Model;
                Subtitle = $"{lease.Model} · {settings.ModelProvider}";
            });
            var observed = new ObservedTools(_tools, call => Post(() => ToolStarted(generation, call)), result => Post(() => ToolEnded(generation, result)));
            var model = chat.WithTools(observed, settings.ChatToolRounds);
            var options = new GenerationOptions { NumPredict = settings.ChatMaxTokens, NumCtx = lease.ContextLength, Stop = settings.ChatStops };
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
            Post(() => Finish(generation, user, Rate(tokens, seconds), cut));
            activity.Complete(Rate(tokens, seconds));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Post(() => Finish(generation, user, Rate(tokens, watch.Elapsed.TotalSeconds), "Stopped."));
            activity.Dispose();
        }
#pragma warning disable CA1031 // The failure is shown in the panel; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            string said = AiErrors.Describe(error, settings);
            Post(() =>
            {
                Finish(generation, user, null, said);
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
        var entry = new ConversationEntry(ConversationRole.Assistant, _segment.ToString());
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

        _segment.Clear();
        _segmentIndex = -1;
        _reply.Append('\n');
        _toolRows[call] = Add(new ConversationEntry(ConversationRole.Tool, call.Name) { Detail = "running…" });
    }

    private void ToolEnded(int generation, ToolResult result)
    {
        FlushIncoming();
        if (generation != _generation || !_toolRows.TryGetValue(result.Call, out int index))
        {
            return;
        }

        Update(index, new ConversationEntry(ConversationRole.Tool, result.Call.Name) { Detail = "→ " + Summarize(result), Succeeded = result.Succeeded });
    }

    private void Finish(int generation, ChatMessage user, string? status, string? problem)
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
                rebuilt.AddRange(Split(entry.Text, CodeEntry));
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

        if (status is not null)
        {
            rebuilt.Add(new ConversationEntry(ConversationRole.Note, status));
        }

        ReplaceFrom(_replyStart, rebuilt);
        Status = status;
        string reply = _reply.ToString().Trim();
        if (reply.Length > 0)
        {
            _history.Add(user);
            _history.Add(new ChatMessage("assistant", reply));
            int limit = _settings().ChatHistory;
            if (_history.Count > limit)
            {
                _history.RemoveRange(0, _history.Count - limit);
            }
        }

        IsBusy = false;
        Save();
    }

    private string BuildContext(AiSettings settings)
    {
        var text = new StringBuilder();
        var chips = Chips;
        if (chips.Count == 0 || Host.ActiveDocument is not { } document)
        {
            return ExtraFiles(text, settings);
        }

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

        return ExtraFiles(text, settings);
    }

    private string ExtraFiles(StringBuilder text, AiSettings settings)
    {
        foreach (var chip in _extra)
        {
            string path = chip.Id["path:".Length..];
            string? content = Host.Documents.FirstOrDefault(d => d.FilePath is { } p && string.Equals(Path.GetFullPath(p), path, StringComparison.OrdinalIgnoreCase)) is { } open
                ? open.Document.Buffer.Current.GetText(0, open.Document.Buffer.Current.Length)
                : File.Exists(path) ? File.ReadAllText(path) : null;
            if (content is not null)
            {
                var lines = content.Split('\n');
                text.Append(CultureInfo.InvariantCulture, $"The file {chip.Text}:\n```\n").AppendJoin('\n', lines.Take(settings.ChatFileLines)).Append("\n```\n");
            }
        }

        return text.Length == 0 ? "" : text.Append('\n').ToString();
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
        _history.Clear();
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

    // The kept chat read and parsed on the thread pool, then shown on the UI thread (only if nothing was written meanwhile).
    private async Task ShowKeptAsync(string path, int generation, TaskCompletionSource shown)
    {
        KeptChat? kept = null;
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
                if (_disposed || generation != _keptGeneration || _history.Count > 0 || Entries.Count > 0)
                {
                    return;
                }

                if (kept is not null)
                {
                    _history.AddRange(kept.Messages);
                    ReplaceFrom(0, [.. kept.Entries.Select(e => e.Role == ConversationRole.Code ? CodeEntry(e.Text, e.Language) : new ConversationEntry(e.Role, e.Text) { Detail = e.Detail })]);
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

    private sealed record KeptEntry(ConversationRole Role, string Text, string? Detail, string? Language);

    private sealed record KeptChat(List<ChatMessage> Messages, List<KeptEntry> Entries);

    // On the thread pool: the kept chat's file read and parsed (null when there is none); a broken one throws to be logged.
    private static KeptChat? ReadKept(string path)
    {
        if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject kept)
        {
            return null;
        }

        var messages = new List<ChatMessage>();
        foreach (var node in kept["messages"] as JsonArray ?? [])
        {
            if (node?["role"]?.GetValue<string>() is { } role && node["content"]?.GetValue<string>() is { } content)
            {
                messages.Add(new ChatMessage(role, content));
            }
        }

        var entries = new List<KeptEntry>();
        foreach (var node in kept["entries"] as JsonArray ?? [])
        {
            if (node?["role"]?.GetValue<string>() is { } role && Enum.TryParse<ConversationRole>(role, out var kind) && node["text"]?.GetValue<string>() is { } text)
            {
                entries.Add(new KeptEntry(kind, text, node["detail"]?.GetValue<string>(), node["language"]?.GetValue<string>()));
            }
        }

        return new KeptChat(messages, entries);
    }

    private void Save()
    {
        if (ChatPath() is not { } path)
        {
            return;
        }

        try
        {
            if (_history.Count == 0 && Entries.Count == 0)
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

            var messages = new JsonArray();
            foreach (var message in _history)
            {
                messages.Add((JsonNode)new JsonObject { ["role"] = message.Role, ["content"] = message.Content });
            }

            var entries = new JsonArray();
            foreach (var entry in Entries)
            {
                entries.Add((JsonNode)new JsonObject { ["role"] = entry.Role.ToString(), ["text"] = entry.Text, ["detail"] = entry.Detail, ["language"] = entry.Language });
            }

            // Serialized here (the entries are the UI thread's), written on the thread pool after the writes before it.
            string json = new JsonObject { ["version"] = 1, ["messages"] = messages, ["entries"] = entries }.ToJsonString();
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
