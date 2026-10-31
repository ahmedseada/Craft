// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;
using Idrak.Text.Generation;

namespace Craft.Ai;

/// <summary>
/// The agent run (plan 1 phase A2, desktop; docs/design/Agent.dc.html): Idrak's <see cref="CodingAgent"/> with its
/// <see cref="CodingTools"/> over a working copy of the open folder (<see cref="AgentWorkspace"/>), so every edit is held as
/// a diff: the changed files are listed (+added −removed) with Review diff (each file's change proposed in the editor, the
/// diff view, one undo step) and Discard all. Every command runs through the command sandbox the setting
/// <see cref="AiSettings.AgentSandbox"/> names in Idrak's <c>CommandSandboxes</c> (plug-in P5's approve-each sandbox asks
/// the user for each); when none is registered under it the agent runs no command and the panel says so. The header shows
/// the turn of the most, the tokens, the rate, the elapsed time and the sandbox; Stop cancels; while it runs, what the user
/// sends is a steering note the agent reads before its next step (<see cref="CodingAgent.Steer"/>).
/// </summary>
public sealed class AiAgentPanel : ConversationPanel, IDisposable
{
    /// <summary>The panel's id.</summary>
    public const string PanelId = "ai.agent";

    private readonly Func<AiSettings> _settings;
    private readonly AiModels _models;
    private readonly List<ConversationEntry> _log = [];
    private readonly Dictionary<string, AgentChange> _changes = new(StringComparer.Ordinal);
    private AiAnswer? _answer;                       // the run's answer for the AI events (raised at its first review), or null
    private bool _judged;
    private readonly Stopwatch _elapsed = new();
    private CancellationTokenSource? _running;
    private CodingAgent? _agent;
    private AgentWorkspace? _copy;
    private ITimer? _ticker;
    private int _assistant = -1;
    private readonly StringBuilder _text = new();
    private int _turn;
    private int _tokens;
    private double _modelSeconds;
    private string _sandboxName = "";
    private bool _waiting;
    private bool _disposed;

    /// <summary>An agent panel in <paramref name="host"/>.</summary>
    public AiAgentPanel(IEditorHost host, Func<AiSettings> settings, AiModels models)
        : base(host, PanelId, "Agent")
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _models = models ?? throw new ArgumentNullException(nameof(models));
    }

    /// <summary>The run going on, or the last one (the tests await it).</summary>
    public Task Run { get; private set; } = Task.CompletedTask;

    /// <summary>The files the agent changed and the user has not settled yet.</summary>
    public IReadOnlyList<AgentChange> Changes => [.. _changes.Values.OrderBy(c => c.Relative, StringComparer.Ordinal)];

    /// <summary>The working copy of the run, or null.</summary>
    public AgentWorkspace? WorkingCopy => _copy;

    /// <inheritdoc />
    public override string Placeholder => IsBusy
        ? "Add a note for the next turn (it reads it before its next step)"
        : "Describe a task for the agent (it works on a copy of the folder; you review every change)";

    /// <inheritdoc />
    public override IReadOnlyList<PanelAction> HeaderActions => IsBusy
        ? [new PanelAction("Stop", _ => { Stop(); return Task.CompletedTask; })]
        : _log.Count > 0 ? [new PanelAction("Clear", _ => { Clear(); return Task.CompletedTask; })] : [];

    /// <inheritdoc />
    public override Task SubmitAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        string message = text.Trim();
        if (message.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (IsBusy && _agent is { } agent)
        {
            agent.Steer(message);
            Log(new ConversationEntry(ConversationRole.User, message) { Detail = "a note for the next turn" });
            return Task.CompletedTask;
        }

        Run = StartAsync(message, cancellationToken);
        return Task.CompletedTask;
    }

    /// <summary>Stops the run (the changes so far stay, to review or discard).</summary>
    public void Stop() => _running?.Cancel();

    /// <summary>
    /// Proposes each changed file's diff in the editor (opening a closed file; a new file opens as a new document), one
    /// proposal per file; the accepted ones leave the list. True when every one was accepted.
    /// </summary>
    public async Task<bool> ReviewAsync(CancellationToken cancellationToken = default)
    {
        bool all = true;
        foreach (var change in Changes)
        {
            all &= await ReviewAsync(change, cancellationToken).ConfigureAwait(true);
        }

        return all;
    }

    /// <summary>Proposes <paramref name="change"/>'s diff in the editor; true when the user accepted it.</summary>
    public async Task<bool> ReviewAsync(AgentChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (_copy is not { } copy)
        {
            return false;
        }

        var document = await Host.OpenDocumentAsync(change.Path, cancellationToken).ConfigureAwait(true);
        if (document is null)
        {
            Log(new ConversationEntry(ConversationRole.Error, $"{change.Relative}: the editor could not open it to show the diff."));
            return false;
        }

        string target = copy.NewText(change.Path);
        var snapshot = document.Document.Buffer.Current;
        string current = snapshot.GetText(0, snapshot.Length);
        var edits = AgentWorkspace.Edits(current, target);
        if (edits.Count == 0)
        {
            copy.Settle(change.Path, current);
            _changes.Remove(change.Relative);
            ShowChanges();
            return true;
        }

        RaiseAnswered();
        var proposal = new ProposedEdits(edits, change.IsNew ? $"Agent: new file {change.Relative}" : $"Agent: {change.Relative}")
        {
            Placement = change.IsNew ? $"a new file: save it as {change.Relative}" : "the agent's copy of the file against the editor's text, line by line",
            Origin = "Agent · " + (Subtitle ?? "the model"),
        };
        bool accepted = await Host.ProposeAsync(document, proposal, cancellationToken).ConfigureAwait(true);
        var now = document.Document.Buffer.Current;
        if (accepted)
        {
            Judge(AiVerdicts.Accepted);
            copy.Settle(change.Path, now.GetText(0, now.Length));
            if (copy.ChangeOf(change.Relative) is { } rest)
            {
                _changes[change.Relative] = rest;      // some hunks only: the rest stays to review
            }
            else
            {
                _changes.Remove(change.Relative);
            }

            Log(new ConversationEntry(ConversationRole.Note, $"{change.Relative}: accepted into the editor (one undo step){(change.IsNew ? "; save it to create the file" : "")}."));
        }

        ShowChanges();
        return accepted;
    }

    /// <summary>Drops every change the agent made (its working copy goes); the user's files were never touched.</summary>
    public void DiscardAll()
    {
        if (IsBusy)
        {
            Stop();
        }

        if (_changes.Count > 0)
        {
            RaiseAnswered();
            Judge(AiVerdicts.Rejected);
        }

        _changes.Clear();
        _copy?.Discard();
        _copy = null;
        Log(new ConversationEntry(ConversationRole.Note, "Discarded every change of the run."));
        ShowChanges();
    }

    // The run's answer for the AI events (the task and the files it changed), raised once, before its first review.
    private void RaiseAnswered()
    {
        if (_answer is not null || !AiEvents.Listening)
        {
            return;
        }

        var files = string.Join("\n", _changes.Values.Select(c => c.IsNew ? $"{c.Relative} (new)" : c.Relative));
        _answer = new AiAnswer(AiEvents.NewId(), AiAnswerKinds.Agent, Title, files)
        {
            Plugin = AiSettings.PluginName,
            Model = Subtitle,
            WorkspaceFolder = Host.WorkspaceFolder,
        };
        _judged = false;
        AiEvents.Answered(_answer);
    }

    // What the user did with the run (the first verdict counts: a file accepted, or every change discarded).
    private void Judge(string verdict)
    {
        if (_answer is { } answer && !_judged)
        {
            _judged = true;
            AiEvents.Judged(new AiVerdict(answer.Id, verdict));
        }
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
        _ticker?.Dispose();
        _copy?.Discard();
    }

    private async Task StartAsync(string task, CancellationToken cancellationToken)
    {
        var settings = _settings();
        if (Host.WorkspaceFolder is not { } workspace)
        {
            Log(new ConversationEntry(ConversationRole.Error, "Open a folder first: the agent works on a copy of the open folder."));
            return;
        }

        _copy?.Discard();
        _copy = null;
        _changes.Clear();
        _answer = null;
        _log.Clear();
        _assistant = -1;
        _text.Clear();
        _turn = 0;
        _tokens = 0;
        _modelSeconds = 0;
        _waiting = false;
        Title = task;
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _running = source;
        _elapsed.Restart();
        IsBusy = true;
        _ticker = TimeProvider.System.CreateTimer(_ => Post(UpdateStatus), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Log(new ConversationEntry(ConversationRole.User, task));
        // The open documents' text as the user sees it, unsaved changes included (read here, on the UI thread).
        var unsaved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in Host.Documents)
        {
            if (document.FilePath is { } path && document.Document.IsDirty)
            {
                unsaved[Path.GetFullPath(path)] = document.Document.Buffer.Current.GetText(0, document.Document.Buffer.Current.Length);
            }
        }

        string end;
        try
        {
            end = await Task.Run(() => RunAgentAsync(task, workspace, unsaved, settings, source.Token), CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            end = "Stopped.";
        }
#pragma warning disable CA1031 // The failure is shown in the panel; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            Host.Log(EditorLogLevel.Error, "The agent run failed.", error);
            Log(new ConversationEntry(ConversationRole.Error, error.Message));
            end = "Failed.";
        }

        _ticker?.Dispose();
        _ticker = null;
        _elapsed.Stop();
        _running = null;
        source.Dispose();
        _agent = null;
        Log(new ConversationEntry(ConversationRole.Note, end + (_changes.Count > 0 ? " Review the changed files below, or discard them." : "")));
        IsBusy = false;
        UpdateStatus();
    }

    // On the thread pool: the model, the working copy, the sandbox, the tools and the agent's loop.
    private async Task<string> RunAgentAsync(string task, string workspace, Dictionary<string, string> unsaved, AiSettings settings, CancellationToken token)
    {
        var lease = await _models.ChatAsync(token).ConfigureAwait(false);
        var chat = lease.Chat ?? throw new NotSupportedException($"The model {lease.Model} has no chat template, so it cannot run as an agent.");
        // Idrak's built-ins (the "process" sandbox, the coding languages of the families' plug-in) are registered by now:
        // the lease's load registered the plug-in's Idrak libraries and the idrakPlugins packages.
        IdrakLibraries.EnsureIncluded();
        var copy = AgentWorkspace.Create(workspace, settings.AgentFolder, settings.AgentCopyIgnore, unsaved, token);
        Post(() =>
        {
            _copy = copy;
            Subtitle = lease.Model;
            Log(new ConversationEntry(ConversationRole.Note, string.Create(CultureInfo.InvariantCulture,
                $"Working on a copy of the folder ({copy.Files:N0} files, {copy.Copy}); your files change only when you accept a diff.")));
        });

        _sandboxName = settings.AgentSandbox;
        ICommandSandbox inner;
        if (CommandSandboxes.Find(settings.AgentSandbox) is { } registered)
        {
            inner = registered;
        }
        else
        {
            string refusal = $"No command sandbox '{settings.AgentSandbox}' is registered (registered: {(CommandSandboxes.Names.Count == 0 ? "none" : string.Join(", ", CommandSandboxes.Names))}): "
                             + "the agent runs no command. Install the approve-each sandbox plug-in, or set CRAFT_PLUGIN_AI_AGENTSANDBOX to a registered one.";
            inner = new RefusingSandbox(settings.AgentSandbox, refusal);
            Post(() => Log(new ConversationEntry(ConversationRole.Error, refusal)));
        }

        var sandbox = new ObservedSandbox(inner, (command, limits) => Post(() => CommandStarted(command, limits)), result => Post(() => CommandEnded(result)));
        var options = new AgentOptions
        {
            MaxRounds = settings.AgentMaxTurns,
            TimeLimit = TimeSpan.FromMinutes(settings.AgentMinutes),
            Sampling = new GenerationOptions { NumPredict = settings.AgentMaxTokens, NumCtx = lease.ContextLength },
            Tools = new CodingToolOptions { Sandbox = inner.Name },
        };
        var tools = new CodingTools(copy.Copy, options.Tools, sandbox);
        var observed = new CountingChatModel(chat, () => Post(TurnStarted), (tokens, seconds) => Post(() => Counted(tokens, seconds)));
        var agent = new CodingAgent(observed, options)
        {
            OnDelta = delta => { if (delta.Content.Length > 0) { Post(() => Said(delta.Content)); } },
            OnToolResult = result => Post(() => ToolDone(result)),
        };
        Post(() => _agent = agent);
        var run = await agent.RunAsync(task, tools, token).ConfigureAwait(false);
        return run.End switch
        {
            CodingAgentEnd.Answered => string.Create(CultureInfo.InvariantCulture, $"Done in {run.Rounds} turns, {run.ToolCalls} tool calls."),
            CodingAgentEnd.OutOfBudget => $"Stopped: {run.Note}.",
            _ => $"Ended: {run.End} {run.Note}".TrimEnd() + ".",
        };
    }

    private void TurnStarted()
    {
        _turn++;
        _assistant = -1;
        _text.Clear();
        UpdateStatus();
    }

    private void Counted(int tokens, double seconds)
    {
        _tokens += tokens;
        _modelSeconds += seconds;
        UpdateStatus();
    }

    private void Said(string piece)
    {
        _text.Append(piece);
        var entry = new ConversationEntry(ConversationRole.Assistant, _text.ToString());
        if (_assistant < 0)
        {
            _assistant = Log(entry);
        }
        else
        {
            _log[_assistant] = entry;
            if (_changes.Count == 0)
            {
                Update(_assistant, entry);
            }
            else
            {
                ShowChanges();
            }
        }
    }

    private void ToolDone(ToolResult result)
    {
        _assistant = -1;
        _text.Clear();
        string what = result.Call.Arguments["path"]?.ToString() ?? result.Call.Arguments["pattern"]?.ToString() ?? "";
        if (result.Call.Name != "run_command")
        {
            Log(new ConversationEntry(ConversationRole.Tool, result.Call.Name)
            {
                Detail = string.Create(CultureInfo.InvariantCulture, $"{what} · turn {_turn} · {Summarize(result, 70)}"),
                Succeeded = result.Succeeded,
            });
        }

        if (result.Call.Name is "edit_file" or "write_file" && result.Call.Arguments["path"]?.ToString() is { } path && _copy is { } copy)
        {
            if (copy.ChangeOf(path) is { } change)
            {
                _changes[change.Relative] = change;
            }
            else
            {
                _changes.Remove(System.IO.Path.GetRelativePath(copy.Workspace, copy.WorkspacePath(path)).Replace('\\', '/'));
            }

            ShowChanges();
        }
    }

    private int _command = -1;

    private void CommandStarted(IReadOnlyList<string> command, CommandLimits limits)
    {
        _waiting = true;
        string programs = limits.Programs is { Count: > 0 } allowed ? string.Join(", ", allowed) : "any";
        _command = Log(new ConversationEntry(ConversationRole.Approval, string.Join(' ', command))
        {
            Detail = string.Create(CultureInfo.InvariantCulture,
                $"turn {_turn} · waiting for the '{_sandboxName}' sandbox (it asks you to approve) · programs: {programs} · time limit {limits.Time.TotalSeconds:F0} s · network {(limits.Network ? "on" : "off")} · folder: the working copy"),
        });
        UpdateStatus();
    }

    private void CommandEnded(CommandResult result)
    {
        _waiting = false;
        if (_command >= 0 && _command < _log.Count)
        {
            var entry = _log[_command];
            string outcome = result.Refused
                ? "refused: " + FirstLine(result.Output)
                : result.TimedOut ? "stopped at the time limit" : string.Create(CultureInfo.InvariantCulture, $"exit {result.ExitCode} · {result.Duration.TotalSeconds:F1} s · {FirstLine(result.Output)}");
            _log[_command] = new ConversationEntry(ConversationRole.Tool, "run_command " + entry.Text) { Detail = outcome, Succeeded = result.Succeeded };
            ShowChanges();
        }

        _command = -1;
        UpdateStatus();
    }

    private static string FirstLine(string text)
    {
        var trimmed = text.Trim();
        int end = trimmed.IndexOf('\n', StringComparison.Ordinal);
        string line = end >= 0 ? trimmed[..end] : trimmed;
        return line.Length <= 100 ? line : string.Concat(line.AsSpan(0, 99), "…");
    }

    private void UpdateStatus()
    {
        if (_disposed)
        {
            return;
        }

        var settings = _settings();
        var elapsed = _elapsed.Elapsed;
        string state = IsBusy ? (_waiting ? "Waiting for you · " : "Running · ") : "";
        Status = string.Create(CultureInfo.InvariantCulture,
            $"{state}Turn {_turn} of {settings.AgentMaxTurns} · {_tokens:N0} tokens · {(_modelSeconds > 0 ? _tokens / _modelSeconds : 0):F0} tok/s · Elapsed {(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2} · Sandbox {(_sandboxName.Length > 0 ? _sandboxName : settings.AgentSandbox)}");
    }

    private int Log(ConversationEntry entry)
    {
        _log.Add(entry);
        if (_changes.Count == 0)
        {
            Add(entry);
        }
        else
        {
            ShowChanges();
        }

        return _log.Count - 1;
    }

    // The entries: the run's log, then the changed files with their actions.
    private void ShowChanges()
    {
        var entries = new List<ConversationEntry>(_log);
        if (_changes.Count > 0)
        {
            entries.Add(new ConversationEntry(ConversationRole.Section, "CHANGED FILES")
            {
                Actions =
                [
                    new PanelAction("Review diff", token => ReviewAsync(token), Primary: true),
                    new PanelAction("Discard all", _ => { DiscardAll(); return Task.CompletedTask; }),
                ],
            });
            foreach (var change in Changes)
            {
                var item = change;
                entries.Add(new ConversationEntry(ConversationRole.File, change.Relative)
                {
                    Detail = change.Counts,
                    Actions = [new PanelAction("Review diff", token => ReviewAsync(item, token))],
                });
            }
        }

        ReplaceFrom(0, entries);
    }

    private void Clear()
    {
        _log.Clear();
        if (_changes.Count == 0)
        {
            _copy?.Discard();
            _copy = null;
        }

        Title = "Agent";
        ShowChanges();
        Status = null;
    }

    /// <summary>A sandbox that refuses every command, saying why (no sandbox registered under the setting's name).</summary>
    private sealed class RefusingSandbox(string name, string reason) : ICommandSandbox
    {
        public string Name { get; } = name;

        public string Summary => "refuses every command: " + reason;

        public string? Check(CommandLimits limits) => null;

        public ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new CommandResult(null, false, reason, TimeSpan.Zero, Refused: true));
    }

    /// <summary>A sandbox that tells the panel about each command before and after it runs, and otherwise is <paramref name="inner"/>.</summary>
    private sealed class ObservedSandbox(ICommandSandbox inner, Action<IReadOnlyList<string>, CommandLimits> started, Action<CommandResult> ended) : ICommandSandbox
    {
        public string Name => inner.Name;

        public string Summary => inner.Summary;

        public string? Check(CommandLimits limits) => inner.Check(limits);

        public async ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
        {
            started(command, limits);
            CommandResult result;
            try
            {
                result = await inner.RunAsync(command, directory, limits, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ended(new CommandResult(null, false, "stopped", TimeSpan.Zero, Refused: true));
                throw;
            }

            ended(result);
            return result;
        }
    }

    /// <summary>A chat model that tells the panel when a turn starts and how many tokens it sampled in how long, and otherwise is <paramref name="inner"/>.</summary>
    private sealed class CountingChatModel(IChatModel inner, Action turn, Action<int, double> counted) : IChatModel
    {
        public IReadOnlySet<string> PartKinds => inner.PartKinds;

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            turn();
            var watch = Stopwatch.StartNew();
            int seen = 0;
            await foreach (var chunk in inner.StreamAsync(request, cancellationToken).ConfigureAwait(false))
            {
                int now = chunk.Done && chunk.Stats is { } stats ? stats.GeneratedTokens : chunk.Generated;
                if (now == 0 && !chunk.Done && chunk.Delta.Content.Length > 0)
                {
                    now = seen + 1;    // a model that does not count: a piece a token
                }

                if (now > seen)
                {
                    counted(now - seen, watch.Elapsed.TotalSeconds);
                    watch.Restart();
                    seen = now;
                }

                yield return chunk;
            }
        }
    }
}
