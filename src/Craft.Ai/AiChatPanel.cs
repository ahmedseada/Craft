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
    private CancellationTokenSource? _running;
    private int _generation;
    private int _segmentIndex = -1;
    private int _replyStart;
    private string? _loadedFolder;
    private string _modelName = "";
    private bool _disposed;

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
        _ = Task.Run(() => StreamAsync(messages, user, settings, generation, source.Token, done), CancellationToken.None);
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
            bool applied = await Host.ProposeAsync(document, proposal, cancellationToken).ConfigureAwait(true);
            string name = document.FilePath is { } path ? Path.GetFileName(path) : "the document";
            Add(new ConversationEntry(ConversationRole.Note, applied ? $"Applied to {name} (one undo step)." : $"Not applied to {name}."));
            return applied;
        }

        Add(new ConversationEntry(ConversationRole.Error, "The document kept changing; apply it again."));
        return false;
    }

    private async Task StreamAsync(List<ChatMessage> messages, ChatMessage user, AiSettings settings, int generation, CancellationToken token, TaskCompletionSource done)
    {
        int tokens = 0;
        var watch = new Stopwatch();
        try
        {
            var lease = await _models.ChatAsync(token).ConfigureAwait(false);
            var chat = lease.Chat ?? throw new NotSupportedException($"The model {lease.Model} has no chat template, so it cannot chat.");
            Post(() =>
            {
                _modelName = lease.Model;
                Subtitle = $"{lease.Model} · {settings.ModelProvider}";
            });
            var observed = new ObservedTools(_tools, call => Post(() => ToolStarted(generation, call)), result => Post(() => ToolEnded(generation, result)));
            var model = chat.WithTools(observed, settings.ChatToolRounds);
            var options = new GenerationOptions { NumPredict = settings.ChatMaxTokens, NumCtx = lease.ContextLength };
            int lastGenerated = 0;
            watch.Start();
            await foreach (var chunk in model.StreamAsync(new ChatRequest(messages, Options: options), token).ConfigureAwait(false))
            {
                int now = chunk.Generated;
                tokens += now > lastGenerated ? now - lastGenerated : now < lastGenerated ? now : chunk.Delta.Content.Length > 0 ? 1 : 0;
                lastGenerated = now;
                string piece = chunk.Delta.Content;
                if (piece.Length > 0)
                {
                    Post(() => Append(generation, piece));
                }
            }

            double seconds = watch.Elapsed.TotalSeconds;
            Post(() => Finish(generation, user, Rate(tokens, seconds), null));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Post(() => Finish(generation, user, Rate(tokens, watch.Elapsed.TotalSeconds), "Stopped."));
        }
#pragma warning disable CA1031 // The failure is shown in the panel; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            Post(() => Finish(generation, user, null, error.Message));
            Host.Log(EditorLogLevel.Error, "The chat reply failed.", error);
        }
        finally
        {
            Post(() => done.TrySetResult());
        }
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
        if (generation != _generation || !_toolRows.TryGetValue(result.Call, out int index))
        {
            return;
        }

        Update(index, new ConversationEntry(ConversationRole.Tool, result.Call.Name) { Detail = "→ " + Summarize(result), Succeeded = result.Succeeded });
    }

    private void Finish(int generation, ChatMessage user, string? status, string? problem)
    {
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
            rebuilt.Add(new ConversationEntry(problem == "Stopped." ? ConversationRole.Note : ConversationRole.Error, problem));
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

    private void LoadIfNeeded()
    {
        if (string.Equals(_loadedFolder, Host.WorkspaceFolder, StringComparison.Ordinal))
        {
            return;
        }

        _loadedFolder = Host.WorkspaceFolder;
        _history.Clear();
        var entries = new List<ConversationEntry>();
        try
        {
            if (ChatPath() is { } path && File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject kept)
            {
                foreach (var node in kept["messages"] as JsonArray ?? [])
                {
                    if (node?["role"]?.GetValue<string>() is { } role && node["content"]?.GetValue<string>() is { } content)
                    {
                        _history.Add(new ChatMessage(role, content));
                    }
                }

                foreach (var node in kept["entries"] as JsonArray ?? [])
                {
                    if (node?["role"]?.GetValue<string>() is { } role && Enum.TryParse<ConversationRole>(role, out var kind) && node["text"]?.GetValue<string>() is { } text)
                    {
                        string? language = node["language"]?.GetValue<string>();
                        entries.Add(kind == ConversationRole.Code ? CodeEntry(text, language) : new ConversationEntry(kind, text) { Detail = node["detail"]?.GetValue<string>() });
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or FormatException)
        {
            Host.Log(EditorLogLevel.Warning, "The kept chat could not be read; it starts empty.", error);
        }

        ReplaceFrom(0, entries);
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
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

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

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, new JsonObject { ["version"] = 1, ["messages"] = messages, ["entries"] = entries }.ToJsonString());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Host.Log(EditorLogLevel.Warning, $"The chat could not be kept in {path}.", error);
        }
    }
}
