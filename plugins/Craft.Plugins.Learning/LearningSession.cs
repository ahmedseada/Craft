// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Plugins.Learning.Abstractions;

namespace Craft.Plugins.Learning;

/// <summary>
/// The learning plug-in in one host (plan 1, phase R). It asks the two consents (the first the first time an AI plug-in
/// answers, the second only after a yes to the first and only once a destination is named), records the AI plug-ins'
/// answers and the user's verdicts as episodes only while the first consent holds, shows the Learning panel and the
/// status marks (recording, sharing, training), trains on demand or when the machine is idle, promotes and rolls back
/// adapters, and shares only after the preview. Called on the host's UI thread; nothing there waits: files, the model,
/// training and sharing run on the thread pool and come back through <see cref="IEditorHost.Dispatch"/>.
/// </summary>
public sealed class LearningSession : IDisposable
{
    /// <summary>The Learning panel's id.</summary>
    public const string PanelId = "learning.panel";

    private const int MostPending = 64;
    private readonly IEditorHost _host;
    private readonly Lock _io = new();                     // the files (episodes, consents, adapters), touched off the UI thread
    private readonly Task<ConsentStore> _consents;
    private readonly List<Pending> _pending = [];
    private readonly ListPanel _panel = new(PanelId, "Learning");
    private readonly StatusItem _recording = new("learning.recording", "● Learning", StatusAlignment.Right, 5, LearningPlugin.ShowCommand);
    private readonly StatusItem _sharing = new("learning.sharing", "↑ Sharing", StatusAlignment.Right, 5, LearningPlugin.ShowCommand);
    private readonly StatusItem _training = new("learning.training", "Learning: training", StatusAlignment.Right, 4, LearningPlugin.StopCommand);
    private readonly Timer _timer;
    private readonly List<string> _marked = [];
    private IDisposable? _recordingShown;
    private IDisposable? _sharingShown;
    private IDisposable? _trainingShown;
    private CancellationTokenSource? _train;
    private string? _askedVersion;                         // the learning text's version asked this session (a changed text asks again)
    private bool _asking;
    private DateTimeOffset _lastActivity = DateTimeOffset.UtcNow;
    private int _newEpisodes;
    private (int Count, long Bytes, string Verdicts, IReadOnlyList<EditorEpisode> Last) _stats = (0, 0, "", []);
    private IReadOnlyList<AdapterVersion> _versions = [];
    private (string Model, string Folder)? _current;
    private string _status = "";
    private bool _disposed;

    /// <summary>A session in <paramref name="host"/> (the plug-in's activation).</summary>
    public LearningSession(IEditorHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        string root = Root(Settings());
        _consents = Task.Run(() => new ConsentStore(root));
        _host.AddPanel(_panel);
        _host.DocumentChanged += OnDocumentChanged;
        _host.DocumentClosed += OnDocumentClosed;
        _timer = new Timer(_ => Post(s => s.Tick(DateTimeOffset.UtcNow)), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        _consents.ContinueWith(_ => Post(s => s.Refresh()), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>The learning folder (episodes, tasks, adapters, consents).</summary>
    public string Folder => Root(Settings());

    /// <summary>The consents, once read (null until then: nothing is recorded or asked before).</summary>
    public ConsentStore? Consents => _consents.IsCompletedSuccessfully ? _consents.Result : null;

    /// <summary>The open workspace's episode store.</summary>
    public EpisodeStore Store => new(Folder, _host.WorkspaceFolder, Settings());

    /// <summary>The open workspace's adapters.</summary>
    public AdapterVersions Versions => new(Store.Folder);

    /// <summary>The answers waiting for their verdict to settle.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Whether episodes are recorded now (the first consent holds for this workspace and no setting turned it off).</summary>
    public bool Recording => LearnState() == ConsentState.Granted;

    /// <summary>The last training's result, or null.</summary>
    public TrainingResult? LastTraining { get; private set; }

    /// <summary>The running training, or null.</summary>
    public Task? Training { get; private set; }

    /// <summary>The last share's plan as previewed (what was confirmed is what was sent), or null.</summary>
    public SharePlan? LastShare { get; private set; }

    /// <summary>The background work the session started last (a file write, a delete, a share): the tests wait on it.</summary>
    public Task Background { get; private set; } = Task.CompletedTask;

    /// <summary>The panel's line of what happened last.</summary>
    public string Status => _status;

    /// <summary>The learning consent's words as they read now.</summary>
    public ConsentText LearnText => ConsentText.Learn(Settings(), Folder, ModelName(Settings()));

    /// <summary>The sharing consent's words as they read now.</summary>
    public ConsentText ShareText => ConsentText.Share(Settings());

    /// <summary>Runs <paramref name="action"/> on the host's UI thread (from any thread).</summary>
    internal void Post(Action<LearningSession> action) => _host.Dispatch(() =>
    {
        if (!_disposed)
        {
            action(this);
        }
    });

    /// <summary>The settings now.</summary>
    public LearningSettings Settings() => LearningSettings.From(_host.Settings);

    // ---- consent ----

    private ConsentState LearnState()
    {
        var settings = Settings();
        return settings.ConsentOff || Consents is not { } consents ? ConsentState.Refused : consents.State(ConsentKinds.Learn, _host.WorkspaceFolder, LearnText);
    }

    private ConsentState ShareState()
    {
        var settings = Settings();
        if (settings.ConsentOff || settings.SharingOff || settings.ShareTo.Length == 0 || Consents is not { } consents || LearnState() != ConsentState.Granted)
        {
            return ConsentState.Refused;
        }

        return consents.State(ConsentKinds.Share, _host.WorkspaceFolder, ShareText);
    }

    /// <summary>
    /// Asks the learning consent (the first screen): what is recorded, where, how much, how long, what training runs and
    /// when, that a learned model only proposes; nothing is pre-chosen and a dismissed question decides nothing. A yes is
    /// stored with its time, scope and the text's version; after it, the sharing consent is asked on its own (only when a
    /// destination is named). True when the user said yes.
    /// </summary>
    public async Task<bool> AskLearnAsync(bool again = false)
    {
        var settings = Settings();
        if (settings.ConsentOff)
        {
            await _host.ShowMessageAsync("Learning is off", $"The setting {PluginSetting.VariableName(LearningSettings.PluginName, "consent")} turns learning off; a setting cannot turn it on.").ConfigureAwait(true);
            return false;
        }

        var consents = await _consents.ConfigureAwait(true);
        if (_asking || (!again && _askedVersion == LearnText.Version))
        {
            return false;
        }

        _asking = true;
        _askedVersion = LearnText.Version;
        try
        {
            while (true)
            {
                var text = LearnText;
                int choice = await _host.AskAsync(new EditorQuestion(text.Title, text.Message,
                    ["Yes, learn in all workspaces", "Yes, learn in this workspace only", "No, don't learn", "Open the folder first"])
                {
                    Details = text.Details,
                    Dismissed = -1,
                }).ConfigureAwait(true);
                if (choice == 3)
                {
                    await OpenFolderAsync().ConfigureAwait(true);
                    continue;
                }

                if (choice is < 0 or > 2)
                {
                    return false;                                      // dismissed, or a host that cannot ask: nothing decided
                }

                bool yes = choice < 2;
                string scope = choice == 1 && _host.WorkspaceFolder is { } workspace ? workspace : ConsentStore.AllWorkspaces;
                await Run(() => consents.Set(ConsentKinds.Learn, scope, yes, text)).ConfigureAwait(true);
                Refresh();
                if (yes)
                {
                    await AskShareAsync().ConfigureAwait(true);
                }

                return yes;
            }
        }
        finally
        {
            _asking = false;
        }
    }

    /// <summary>
    /// Asks the sharing consent (the second screen), never with the first: only after a yes to learning and only once a
    /// destination is named (no default: nothing is shared until one is). True when the user said yes.
    /// </summary>
    public async Task<bool> AskShareAsync()
    {
        var settings = Settings();
        if (settings.ConsentOff || settings.SharingOff || settings.ShareTo.Length == 0 || LearnState() != ConsentState.Granted)
        {
            return false;
        }

        var consents = await _consents.ConfigureAwait(true);
        var text = ShareText;
        int choice = await _host.AskAsync(new EditorQuestion(text.Title, text.Message, [$"Yes, allow sharing to {settings.ShareTo}", "No, don't share"])
        {
            Details = text.Details,
            Dismissed = -1,
        }).ConfigureAwait(true);
        if (choice is < 0 or > 1)
        {
            return false;
        }

        string scope = _host.WorkspaceFolder ?? ConsentStore.AllWorkspaces;
        await Run(() => consents.Set(ConsentKinds.Share, scope, choice == 0, text)).ConfigureAwait(true);
        Refresh();
        return choice == 0;
    }

    /// <summary>
    /// Withdraws the learning consent (one switch): recording stops at once (the answers waiting are dropped), nothing more
    /// is shared, and the user chooses whether the stored episodes, tasks and adapters are deleted (what was freed is said).
    /// </summary>
    public async Task WithdrawAsync()
    {
        var consents = await _consents.ConfigureAwait(true);
        _pending.Clear();
        StopTraining();
        var learn = LearnText;
        var share = ShareText;
        await Run(() =>
        {
            consents.Withdraw(ConsentKinds.Learn, learn);
            consents.Withdraw(ConsentKinds.Share, share);
        }).ConfigureAwait(true);
        Refresh();
        var store = Store;
        long bytes = await Task.Run(() => AdapterVersions.FolderBytes(store.Folder)).ConfigureAwait(true);
        int choice = await _host.AskAsync(new EditorQuestion("Learning stopped",
            "Nothing more is recorded or shared. What was stored stays on this machine unless you delete it.",
            ["Keep the stored data", $"Delete it ({SharePlan.Bytes(bytes)})"])
        {
            Details = [$"Where: {store.Folder}"],
            Dismissed = 0,
        }).ConfigureAwait(true);
        if (choice == 1)
        {
            long freed = await Run(() => DeleteWorkspaceData(store)).ConfigureAwait(true);
            _newEpisodes = 0;
            _status = $"Deleted the stored episodes, tasks and adapters: {SharePlan.Bytes(freed)} freed.";
            _current = null;
            ModelAdapters.RaiseChanged();
            Refresh();
            await _host.ShowMessageAsync("Learning: deleted", _status).ConfigureAwait(true);
        }
    }

    /// <summary>Withdraws the sharing consent: nothing more is shared.</summary>
    public async Task StopSharingAsync()
    {
        var consents = await _consents.ConfigureAwait(true);
        var text = ShareText;
        await Run(() => consents.Withdraw(ConsentKinds.Share, text)).ConfigureAwait(true);
        _status = "Sharing stopped.";
        Refresh();
    }

    // ---- the AI events ----

    /// <summary>
    /// An AI plug-in answered. Before the first consent nothing is kept (the first answer asks it, once a session); with it,
    /// the answer waits for its verdict (read from the document for a completion, told for the others).
    /// </summary>
    internal void OnAnswered(AiAnswer answer)
    {
        _lastActivity = DateTimeOffset.UtcNow;
        var state = LearnState();
        if (state != ConsentState.Granted)
        {
            if (state is ConsentState.Undecided or ConsentState.Changed && Consents is not null && !Settings().ConsentOff)
            {
                _ = AskLearnAsync();
            }

            return;
        }

        if (!Settings().Record.Contains(answer.Kind))
        {
            return;
        }

        // A new completion in the same text replaces one never taken: that one was passed over.
        if (answer.Kind == AiAnswerKinds.Completion)
        {
            foreach (var old in _pending.Where(p => p.Kind == AiAnswerKinds.Completion && p.State == PendingState.Proposed && ReferenceEquals(p.Buffer, answer.Snapshot?.Buffer)).ToList())
            {
                Settle(old);
            }
        }

        if (_pending.Count >= MostPending)
        {
            Settle(_pending[0]);
        }

        var pending = new Pending(answer)
        {
            Buffer = answer.Kind == AiAnswerKinds.Completion ? answer.Snapshot?.Buffer : null,
            Start = answer.Position,
            State = PendingState.Proposed,
        };

        // A ghost taken while it still streamed (Tab cancels the request, whose end is told after the insert): the text
        // grown at the caret since the answer's snapshot that starts the answer is what was taken.
        if (pending.Buffer is { } buffer && answer.Snapshot is { } then && buffer.Current is { } now && now.Version != then.Version && answer.Position >= 0
            && answer.Position <= now.Length)
        {
            int most = Math.Min(answer.Answer.Length, Math.Min(now.Length - then.Length, now.Length - answer.Position));
            int taken = 0;
            while (taken < most && now[answer.Position + taken] == answer.Answer[taken])
            {
                taken++;
            }

            if (taken > 0)
            {
                pending.State = PendingState.Applied;
                pending.Length = taken;
            }
        }

        _pending.Add(pending);
    }

    /// <summary>The user decided about an answer: a rejection settles it at once; an acceptance is followed in the document until it settles.</summary>
    internal void OnJudged(AiVerdict verdict)
    {
        _lastActivity = DateTimeOffset.UtcNow;
        var pending = _pending.FirstOrDefault(p => p.Answer.Id == verdict.AnswerId);
        if (pending is null)
        {
            return;                                                    // answered before consent, or withdrawn since
        }

        if (verdict.Verdict != AiVerdicts.Accepted || verdict.Document is null || verdict.Start < 0)
        {
            pending.Verdict = verdict.Verdict;
            pending.Kept = verdict.Kept;
            Settle(pending);
            return;
        }

        pending.State = PendingState.Applied;
        pending.Buffer = verdict.Document.Document.Buffer;
        pending.Start = verdict.Start;
        pending.Length = verdict.Length;
        pending.Touched = DateTimeOffset.UtcNow;
    }

    private void OnDocumentChanged(object? sender, EditorDocumentChangedEventArgs e)
    {
        _lastActivity = DateTimeOffset.UtcNow;
        var buffer = e.Document.Document.Buffer;
        var edits = e.Change.Edits;
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            if (!ReferenceEquals(p.Buffer, buffer))
            {
                continue;
            }

            if (p.State == PendingState.Proposed)
            {
                if (edits is [{ Length: 0, Text.Length: > 0 } typed] && typed.Start == p.Start)
                {
                    p.State = p.Answer.Answer.StartsWith(typed.Text, StringComparison.Ordinal) ? PendingState.Applied : PendingState.Typed;
                    p.Length = typed.Text.Length;
                    p.Touched = DateTimeOffset.UtcNow;
                }
                else if (edits.All(edit => edit.End <= p.Start && !(edit.Length == 0 && edit.Start == p.Start)))
                {
                    p.Start += edits.Sum(edit => edit.Delta);              // typed before it: it moves
                }
                else
                {
                    Settle(p);                                         // typed elsewhere around it: passed over
                }

                continue;
            }

            // A completion taken a word at a time: the next word inserted right after the part taken.
            if (p.State == PendingState.Applied && p.Kind == AiAnswerKinds.Completion && !p.Edited && edits is [{ Length: 0 } more]
                && more.Start == p.Start + p.Length && p.Answer.Answer.AsSpan(Math.Min(p.Length, p.Answer.Answer.Length)).StartsWith(more.Text, StringComparison.Ordinal)
                && more.Text.Length > 0)
            {
                p.Length += more.Text.Length;
                p.Touched = DateTimeOffset.UtcNow;
                continue;
            }

            Follow(p, edits, e.Change.Reason);
        }
    }

    // Moves the answer's range through the change; an edit inside it (or, for text typed in its place, at its end) marks it.
    private static void Follow(Pending p, IReadOnlyList<TextEdit> edits, TextChangeReason reason)
    {
        int start = p.Start, end = p.Start + p.Length, shift = 0, grow = 0;
        bool inside = false;
        foreach (var edit in edits)
        {
            bool before = edit.End < start || (edit.End == start && (edit.Length > 0 || p.State != PendingState.Typed || edit.Start < start));
            if (edit.End <= start && before)
            {
                shift += edit.Delta;
                continue;
            }

            bool atEnd = edit.Start == end && edit.Length == 0;
            if (edit.Start > end || (atEnd && p.State != PendingState.Typed))
            {
                continue;                                              // after it
            }

            inside = true;
            int from = Math.Max(edit.Start, start), to = Math.Min(edit.End, end);
            grow += edit.Text.Length - Math.Max(0, to - from);
        }

        p.Start = start + shift;
        p.Length = Math.Max(0, end - start + grow);
        if (inside)
        {
            p.Edited = true;
            p.Touched = DateTimeOffset.UtcNow;
            if (reason == TextChangeReason.Undo)
            {
                p.Undone = true;
            }
            else if (reason is TextChangeReason.Edit or TextChangeReason.Redo)
            {
                p.Undone = false;
            }
        }
    }

    private void OnDocumentClosed(object? sender, EditorDocumentEventArgs e)
    {
        var buffer = e.Document.Document.Buffer;
        foreach (var p in _pending.Where(p => ReferenceEquals(p.Buffer, buffer)).ToList())
        {
            Settle(p);
        }
    }

    /// <summary>Settles every answer waiting (its verdict as the text reads now) and writes their episodes; what the tests call instead of waiting.</summary>
    public void SettleAll()
    {
        foreach (var p in _pending.ToList())
        {
            Settle(p);
        }
    }

    private void Tick(DateTimeOffset now)
    {
        var settings = Settings();
        var settle = TimeSpan.FromSeconds(settings.SettleSeconds);
        foreach (var p in _pending.Where(p => p.State != PendingState.Proposed || p.Kind == AiAnswerKinds.Completion).ToList())
        {
            if (now - p.Touched >= settle)
            {
                Settle(p);
            }
        }

        if (settings.Schedule == "idle" && Training is null && _newEpisodes >= settings.MinEpisodes && now - _lastActivity >= TimeSpan.FromMinutes(settings.IdleMinutes) && Recording)
        {
            _ = TrainAsync(manual: false);
        }
    }

    // The verdict as the text reads now, the episode written (off the UI thread) while the consent still holds.
    private void Settle(Pending p)
    {
        _pending.Remove(p);
        if (LearnState() != ConsentState.Granted)
        {
            return;
        }

        string answer = p.Answer.Answer;
        string verdict;
        string? kept = null;
        double edited = 0;
        if (p.Verdict is { } told)
        {
            verdict = told;
            kept = p.Kept;
        }
        else if (p.State == PendingState.Proposed)
        {
            verdict = AiVerdicts.Rejected;                             // never taken
        }
        else
        {
            string now = p.Buffer is { } buffer && p.Start >= 0 && p.Start + p.Length <= buffer.Current.Length ? buffer.Current.GetText(p.Start, p.Length) : "";
            if (p.State == PendingState.Typed)
            {
                verdict = AiVerdicts.Rejected;
                kept = now.Length > 0 ? now : null;                    // what the user wrote in its place
            }
            else if (p.Undone && !now.Equals(answer, StringComparison.Ordinal))
            {
                verdict = AiVerdicts.Undone;
                kept = p.Answer.Before is { Length: > 0 } before ? before : now.Length > 0 ? now : null;
            }
            else if (now.Equals(answer, StringComparison.Ordinal))
            {
                verdict = AiVerdicts.Accepted;
            }
            else
            {
                verdict = AiVerdicts.Edited;
                kept = now;
                edited = Distance(answer, now);
            }
        }

        var a = p.Answer;
        var episode = new EditorEpisode(a.Id, a.Time, a.Kind, a.Prompt, answer, verdict)
        {
            Plugin = a.Plugin,
            Model = a.Model,
            File = a.FilePath is { } path ? Relative(a.WorkspaceFolder, path) : null,
            Language = a.LanguageName,
            Kept = kept,
            Edited = edited,
            PromptIds = a.PromptIds,
            AnswerIds = a.AnswerIds,
        };
        var store = Store;
        _newEpisodes++;
        // Chained: the episodes are written in the order they settled, one after another, off the UI thread.
        Background = Background.ContinueWith(_ =>
        {
            lock (_io)
            {
                store.Append(episode);
            }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default)
            .ContinueWith(_ => Post(s => s.Refresh()), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // ---- the panel and the status bar ----

    /// <summary>Shows the Learning panel.</summary>
    public void Show()
    {
        Refresh();
        _host.ShowPanel(PanelId);
    }

    /// <summary>Reads the panel's figures off the UI thread, then redraws it and the status marks.</summary>
    public void Refresh()
    {
        var store = Store;
        var versions = Versions;
        string? workspaceModel = ModelName(Settings());
        _ = Task.Run(() =>
        {
            List<EditorEpisode> all;
            IReadOnlyList<AdapterVersion> kept;
            long bytes;
            lock (_io)
            {
                all = store.All();
                bytes = store.Bytes;
                kept = versions.All;
            }

            var current = versions.Current;
            string verdicts = string.Join(" · ", all.GroupBy(e => e.Verdict, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Key} {g.Count()}")));
            Post(s =>
            {
                s._stats = (all.Count, bytes, verdicts, all.TakeLast(10).Reverse().ToList());
                s._versions = kept;
                s._current = current is null ? null : (current.BaseModel, current.Folder);
                s.Draw(workspaceModel);
            });
        });
        Draw(workspaceModel);
    }

    private void Draw(string? model)
    {
        var settings = Settings();
        var items = new List<PanelItem>();
        var learn = Consents?.Find(ConsentKinds.Learn, _host.WorkspaceFolder);
        var state = LearnState();
        items.Add(state switch
        {
            ConsentState.Granted => new PanelItem("Learn on this machine: on", $"{(learn!.Scope == ConsentStore.AllWorkspaces ? "all workspaces" : "this workspace")} · since {learn.Time.ToLocalTime():yyyy-MM-dd HH:mm} · text {learn.Version} · click to stop", LearningPlugin.WithdrawCommand),
            ConsentState.Changed => new PanelItem("Learn on this machine: off until you answer again", "what is recorded or where changed since you said yes", LearningPlugin.ConsentCommand),
            _ when settings.ConsentOff => new PanelItem("Learn on this machine: off", $"the setting {PluginSetting.VariableName(LearningSettings.PluginName, "consent")} is off"),
            _ => new PanelItem("Learn on this machine: off", "nothing is recorded · click to choose", LearningPlugin.ConsentCommand),
        });
        var shareState = ShareState();
        items.Add(shareState == ConsentState.Granted
            ? new PanelItem($"Share: on, to {settings.ShareTo}", $"{string.Join(", ", settings.ShareWhat)} · every share previewed first · click to share now", LearningPlugin.ShareCommand)
            : settings.ShareTo.Length == 0
                ? new PanelItem("Share: off", $"nothing is shared until you name a destination ({PluginSetting.VariableName(LearningSettings.PluginName, "shareTo")})")
                : new PanelItem("Share: off", $"to {settings.ShareTo} once you agree · click to choose", LearningPlugin.ShareCommand));
        if (shareState == ConsentState.Granted)
        {
            items.Add(new PanelItem("Stop sharing", null, LearningPlugin.StopSharingCommand));
        }

        items.Add(new PanelItem("Folder", Folder, LearningPlugin.OpenFolderCommand));
        items.Add(new PanelItem(string.Create(CultureInfo.InvariantCulture, $"Episodes: {_stats.Count} ({SharePlan.Bytes(_stats.Bytes)} of {settings.CapMegabytes} MiB)"),
            _stats.Verdicts.Length == 0 ? "none yet" : _stats.Verdicts + $" · {_pending.Count} waiting", LearningPlugin.ExportCommand));
        foreach (var episode in _stats.Last)
        {
            string answer = episode.Answer.ReplaceLineEndings(" ⏎ ");
            items.Add(new PanelItem($"  {episode.Kind} · {episode.Verdict}{(episode.File is { } f ? " · " + f : "")}", answer.Length > 60 ? answer[..60] + "…" : answer));
        }

        items.Add(new PanelItem("Export the episodes", null, LearningPlugin.ExportCommand));
        items.Add(new PanelItem("Delete the episodes", null, LearningPlugin.DeleteAllCommand));
        items.Add(Training is null
            ? new PanelItem("Train now", $"{settings.Trainer} · {model ?? "no model set"} · {(settings.Schedule == "idle" ? $"also when idle {settings.IdleMinutes} min" : "on demand only")}", LearningPlugin.TrainCommand)
            : new PanelItem("Stop training", _training.Text, LearningPlugin.StopCommand));
        items.Add(new PanelItem("Make tasks from the failing tests", $"through the sandbox {settings.Sandbox}", LearningPlugin.TasksCommand));
        items.Add(new PanelItem("Mark the test at the caret", _marked.Count == 0 ? null : string.Join(", ", _marked), LearningPlugin.MarkTestCommand));
        var current = _versions.LastOrDefault(v => _current is { } c && v.Folder == c.Folder);
        items.Add(new PanelItem(current is null ? "Adapter: none (the base model answers)" : $"Adapter: v{current.Number} (current)", current?.Note));
        foreach (var version in _versions.Reverse())
        {
            items.Add(new PanelItem(string.Create(CultureInfo.InvariantCulture, $"  v{version.Number} · {version.Time.ToLocalTime():yyyy-MM-dd HH:mm} · {(version.Promoted ? "promoted" : "kept")}"),
                string.Create(CultureInfo.InvariantCulture, $"held-out {version.ScoreBefore:F3} → {version.ScoreAfter:F3} · {version.Pairs} pairs · loss {version.FirstLoss:F3} → {version.LastLoss:F3}")));
        }

        if (_current is not null)
        {
            items.Add(new PanelItem("Roll back the adapter", "one step back (to the previous promoted one, or the base model)", LearningPlugin.RollbackCommand));
        }

        if (_versions.Count > 0)
        {
            items.Add(new PanelItem("Delete an adapter version", null, LearningPlugin.DeleteVersionCommand));
        }

        if (_status.Length > 0)
        {
            items.Add(new PanelItem(_status));
        }

        _panel.SetItems(items);
        Show(ref _recordingShown, _recording, state == ConsentState.Granted);
        _recording.Tooltip = state == ConsentState.Granted ? $"Learning: the AI's answers and what you do with them are recorded in {Folder}. Click for the Learning panel." : null;
    }

    private void Show(ref IDisposable? shown, IStatusItem item, bool visible)
    {
        if (visible && shown is null)
        {
            shown = _host.AddStatusItem(item);
        }
        else if (!visible && shown is not null)
        {
            shown.Dispose();
            shown = null;
        }
    }

    // ---- folder, export, delete ----

    /// <summary>Opens the learning folder outside the editor (or shows its path when the host cannot).</summary>
    public async Task OpenFolderAsync()
    {
        string folder = Folder;
        await Task.Run(() => Directory.CreateDirectory(folder)).ConfigureAwait(true);
        if (!await _host.OpenExternalAsync(folder).ConfigureAwait(true))
        {
            await _host.ShowMessageAsync("The learning folder", folder).ConfigureAwait(true);
        }
    }

    /// <summary>Exports the workspace's episodes (JSON lines) into the learning folder's exports; says where.</summary>
    public async Task ExportAsync()
    {
        var store = Store;
        string path = Path.Combine(Folder, "exports", $"episodes-{DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.jsonl");
        string written = await Run(() => store.Export(path)).ConfigureAwait(true);
        _status = $"Exported the episodes to {written}.";
        Refresh();
        await _host.ShowMessageAsync("Learning: exported", _status).ConfigureAwait(true);
    }

    /// <summary>Deletes the workspace's episodes (asked first); says how much was freed.</summary>
    public async Task DeleteEpisodesAsync()
    {
        var store = Store;
        long bytes = await Task.Run(() => store.Bytes).ConfigureAwait(true);
        int choice = await _host.AskAsync(new EditorQuestion("Delete the episodes?", $"Every recorded episode of this workspace ({SharePlan.Bytes(bytes)}) is deleted. Adapters stay.",
            ["Delete them", "Keep them"]) { Dismissed = 1 }).ConfigureAwait(true);
        if (choice != 0)
        {
            return;
        }

        long freed = await Run(store.DeleteAll).ConfigureAwait(true);
        _newEpisodes = 0;
        _status = $"Deleted the episodes: {SharePlan.Bytes(freed)} freed.";
        Refresh();
    }

    private static long DeleteWorkspaceData(EpisodeStore store)
    {
        long freed = AdapterVersions.FolderBytes(store.Folder);
        if (Directory.Exists(store.Folder))
        {
            Directory.Delete(store.Folder, recursive: true);
        }

        return freed;
    }

    // ---- training ----

    /// <summary>
    /// Trains now (or when idle, not <paramref name="manual"/>): the user's model as an adapter, by the setting's trainer,
    /// off the UI thread, with its progress in the status bar and the panel; stoppable at every step. A promoted adapter
    /// is what the AI plug-ins load next (<see cref="ModelAdapters"/>).
    /// </summary>
    public async Task TrainAsync(bool manual)
    {
        if (Training is not null)
        {
            return;
        }

        var settings = Settings();
        if (LearnState() != ConsentState.Granted)
        {
            if (manual)
            {
                await _host.ShowMessageAsync("Learning", "Learning is off: nothing was recorded to train on. Say yes to learning first (Learning: ask me about learning again).").ConfigureAwait(true);
            }

            return;
        }

        if (ModelName(settings) is not { } model)
        {
            if (manual)
            {
                await _host.ShowMessageAsync("Learning", $"No model to train: set {PluginSetting.VariableName(LearningSettings.PluginName, "model")} or the AI plug-in's model.").ConfigureAwait(true);
            }

            return;
        }

        var store = Store;
        var versions = Versions;
        string? workspace = _host.WorkspaceFolder;
        var source = new CancellationTokenSource();
        _train = source;
        var progress = _host.StartProgress("Learning: training the adapter");
        Show(ref _trainingShown, _training, true);
        _training.Text = $"Learning: loading {model}";
        var reports = new Progress(p => Post(s =>
        {
            s._training.Text = p.Line;
            progress.Report(p.Steps > 0 ? (double)p.Step / p.Steps : null, p.Line);
        }));
        _newEpisodes = 0;
        var run = Task.Run(() =>
        {
            List<EditorEpisode> episodes;
            lock (_io)
            {
                episodes = store.All();
            }

            using var loaded = LearningTrainer.LoadModel(settings, model);
            reports.Report(new TrainingProgress("loaded on", 0, settings.Steps, float.NaN, TimeSpan.Zero, null, loaded.Device.ToString(), 0, 0));
            if (settings.Trainer == "preference" || workspace is null)
            {
                return LearningTrainer.TrainPreferences(loaded, model, episodes, versions, settings, reports, source.Token);
            }

            string suite = Path.Combine(store.Folder, "tasks"), held = Path.Combine(store.Folder, "tasks-held");
            if (!Directory.Exists(suite))
            {
                return new TrainingResult(null, "No tasks yet: run Learning: make tasks from the failing tests.", [], [], []);
            }

            var result = LearningTrainer.TrainTasks(loaded, model, suite, Directory.Exists(held) ? held : null, versions, settings, reports, source.Token);
            if (settings.Trainer == "both" && !source.IsCancellationRequested)
            {
                var preference = LearningTrainer.TrainPreferences(loaded, model, episodes, versions, settings, reports, source.Token);
                return preference with { Message = result.Message + " " + preference.Message };
            }

            return result;
        }, source.Token);
        Training = run;
        Refresh();
        TrainingResult? outcome = null;
        string message;
        try
        {
            outcome = await run.ConfigureAwait(true);
            message = outcome.Message;
        }
        catch (OperationCanceledException)
        {
            message = "Training stopped: no adapter written, the current one stays.";
        }
#pragma warning disable CA1031 // A training's failure is shown to the user; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            message = $"Training failed: {error.Message}";
            _host.Log(EditorLogLevel.Error, message, error);
        }
        finally
        {
            progress.Dispose();
            Show(ref _trainingShown, _training, false);
            Training = null;
            _train = null;
            source.Dispose();
        }

        LastTraining = outcome;
        _status = message;
        if (outcome?.Promoted == true)
        {
            _current = (outcome.Version!.BaseModel, outcome.Version.Folder);
            ModelAdapters.RaiseChanged();
        }

        Refresh();
        if (manual)
        {
            await _host.ShowMessageAsync("Learning: training", message).ConfigureAwait(true);
        }
    }

    /// <summary>Stops the running training at its next step.</summary>
    public void StopTraining() => _train?.Cancel();

    /// <summary>Goes back to the adapter promoted before the current one (none: the base model); the AI plug-ins load it next.</summary>
    public async Task RollbackAsync()
    {
        var versions = Versions;
        var now = await Run(versions.Rollback).ConfigureAwait(true);
        _current = now is null ? null : (now.BaseModel, now.Folder);
        _status = now is null ? "Rolled back: the base model answers (no adapter)." : $"Rolled back to v{now.Number}.";
        ModelAdapters.RaiseChanged();
        Refresh();
    }

    /// <summary>Deletes an adapter version the user picks (the current one too: then the one before it is current).</summary>
    public async Task DeleteVersionAsync()
    {
        var versions = Versions;
        var all = await Task.Run(() => versions.All).ConfigureAwait(true);
        if (all.Count == 0)
        {
            return;
        }

        int choice = await _host.AskAsync(new EditorQuestion("Delete an adapter version", "Its files are deleted; the others stay.",
            [.. all.Select(v => string.Create(CultureInfo.InvariantCulture, $"v{v.Number} ({(v.Promoted ? "promoted" : "kept")}, {v.Time.ToLocalTime():yyyy-MM-dd HH:mm})")), "Cancel"])
        { Dismissed = all.Count }).ConfigureAwait(true);
        if (choice < 0 || choice >= all.Count)
        {
            return;
        }

        int number = all[choice].Number;
        long freed = await Run(() => versions.Delete(number)).ConfigureAwait(true);
        var current = await Task.Run(() => versions.Current).ConfigureAwait(true);
        _current = current is null ? null : (current.BaseModel, current.Folder);
        _status = string.Create(CultureInfo.InvariantCulture, $"Deleted v{number}: {SharePlan.Bytes(freed)} freed.");
        ModelAdapters.RaiseChanged();
        Refresh();
    }

    /// <summary>The promoted adapter of the open workspace for <paramref name="model"/>, or null (any thread: it reads what the session last read).</summary>
    public string? AdapterFor(string model) => _current is { } current && string.Equals(current.Model, model, StringComparison.Ordinal) ? current.Folder : null;

    // ---- tasks ----

    /// <summary>
    /// Makes coding tasks from the workspace's failing tests (<c>dotnet test</c> through the setting's sandbox, which asks
    /// you first by default) and the tests you marked: a suite for training and a held-out one, in task.json form.
    /// </summary>
    public async Task MakeTasksAsync()
    {
        if (_host.WorkspaceFolder is not { } workspace)
        {
            await _host.ShowMessageAsync("Learning: tasks", "Open a folder first: the tasks are made from its tests.").ConfigureAwait(true);
            return;
        }

        if (LearnState() != ConsentState.Granted)
        {
            await _host.ShowMessageAsync("Learning: tasks", "Learning is off: say yes to learning first.").ConfigureAwait(true);
            return;
        }

        var settings = Settings();
        var store = Store;
        var marked = _marked.Select(m => new WorkspaceTest(m, "marked by you")).ToList();
        using var progress = _host.StartProgress("Learning: running the tests");
        string message;
        try
        {
            message = await Task.Run(async () =>
            {
                var (failing, run) = await WorkspaceTasks.FindFailingAsync(workspace, settings.Sandbox, Path.Combine(store.Folder, "test-results"), TimeSpan.FromMinutes(10),
                    CancellationToken.None).ConfigureAwait(false);
                var tests = failing.Concat(marked.Where(m => failing.All(f => f.Name != m.Name))).ToList();
                if (tests.Count == 0)
                {
                    return run.Refused ? $"The tests did not run: {run.Output}" : "No failing or marked test: nothing to make a task of.";
                }

                var train = tests.Where(t => !LearningTrainer.IsHeldOut(t.Name, settings.HeldOutShare)).ToList();
                var held = tests.Where(t => LearningTrainer.IsHeldOut(t.Name, settings.HeldOutShare)).ToList();
                lock (_io)
                {
                    WorkspaceTasks.Write(Path.Combine(store.Folder, "tasks"), workspace, train.Count > 0 ? train : held);
                    WorkspaceTasks.Write(Path.Combine(store.Folder, "tasks-held"), workspace, train.Count > 0 ? held : []);
                }

                return string.Create(CultureInfo.InvariantCulture,
                    $"{tests.Count} task(s) in {Path.Combine(store.Folder, "tasks")} ({held.Count} held out): {string.Join(", ", tests.Select(t => t.Name))}. "
                    + $"The idrak tool reads them: idrak reinforce MODEL --env csharp --env-option suite={Path.Combine(store.Folder, "tasks")}");
            }).ConfigureAwait(true);
        }
        catch (NotRegisteredException error)
        {
            message = error.Message;
        }

        _status = message;
        Refresh();
        await _host.ShowMessageAsync("Learning: tasks", message).ConfigureAwait(true);
    }

    /// <summary>Marks the test named at the caret (the word there, or the selection) as a task.</summary>
    public Task MarkTestAsync()
    {
        if (_host.ActiveDocument is not { } document)
        {
            return Task.CompletedTask;
        }

        var snapshot = document.Document.Buffer.Current;
        var selection = document.Selection;
        int start = Math.Min(selection.Anchor, selection.Caret), end = Math.Max(selection.Anchor, selection.Caret);
        if (start == end)
        {
            while (start > 0 && IsName(snapshot[start - 1]))
            {
                start--;
            }

            while (end < snapshot.Length && IsName(snapshot[end]))
            {
                end++;
            }
        }

        string name = snapshot.GetText(start, end - start).Trim();
        if (name.Length > 0 && !_marked.Contains(name))
        {
            _marked.Add(name);
            _status = $"Marked {name}: the next Make tasks includes it.";
            Refresh();
        }

        return Task.CompletedTask;

        static bool IsName(char c) => char.IsLetterOrDigit(c) || c is '_' or '.';
    }

    // ---- sharing ----

    /// <summary>
    /// Shares: only with both consents and a named destination; the plan (every item, its size, a preview, the files and
    /// paths it touches, and what was left out) shown first and confirmed; then exactly those items sent, the sharing mark
    /// shown while it runs.
    /// </summary>
    public async Task ShareAsync()
    {
        var settings = Settings();
        if (LearnState() != ConsentState.Granted)
        {
            await _host.ShowMessageAsync("Learning: share", "Nothing is shared without the learning consent.").ConfigureAwait(true);
            return;
        }

        if (settings.ShareTo.Length == 0)
        {
            await _host.ShowMessageAsync("Learning: share", $"Nothing is shared until you name a destination: set {PluginSetting.VariableName(LearningSettings.PluginName, "shareTo")}.").ConfigureAwait(true);
            return;
        }

        if (ShareState() != ConsentState.Granted && !await AskShareAsync().ConfigureAwait(true))
        {
            return;
        }

        IShareDestination destination;
        string target;
        try
        {
            (string kind, target) = ShareDestinations.Parse(settings.ShareTo);
            destination = ShareDestinations.Get(kind);
        }
        catch (Exception error) when (error is ArgumentException or NotRegisteredException)
        {
            await _host.ShowMessageAsync("Learning: share", error.Message).ConfigureAwait(true);
            return;
        }

        var store = Store;
        var versions = Versions;
        string? workspace = _host.WorkspaceFolder;
        var plan = await Task.Run(() =>
        {
            lock (_io)
            {
                var episodes = store.All();
                return SharePlans.Make(settings, workspace, episodes, versions.Current?.Folder, SharePlans.Scores(episodes, versions.All));
            }
        }).ConfigureAwait(true);
        int choice = await _host.AskAsync(new EditorQuestion("Share these items?",
            string.Create(CultureInfo.InvariantCulture, $"{plan.Items.Count} item(s), {SharePlan.Bytes(plan.Size)}, would go to {settings.ShareTo}. Nothing else is sent."),
            [string.Create(CultureInfo.InvariantCulture, $"Share these {plan.Items.Count} item(s)"), "Cancel"])
        {
            Details = plan.PreviewLines(),
            Dismissed = 1,
        }).ConfigureAwait(true);
        if (choice != 0 || plan.Items.Count == 0)
        {
            return;
        }

        LastShare = plan;
        Show(ref _sharingShown, _sharing, true);
        try
        {
            await (Background = Task.Run(() => destination.SendAsync(target, plan.Items, CancellationToken.None))).ConfigureAwait(true);
            _status = string.Create(CultureInfo.InvariantCulture, $"Shared {plan.Items.Count} item(s), {SharePlan.Bytes(plan.Size)}, to {settings.ShareTo}.");
        }
#pragma warning disable CA1031 // A share's failure is shown to the user; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _status = $"The share failed: {error.Message}";
            _host.Log(EditorLogLevel.Error, _status, error);
        }
        finally
        {
            Show(ref _sharingShown, _sharing, false);
        }

        Refresh();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        StopTraining();
        _host.DocumentChanged -= OnDocumentChanged;
        _host.DocumentClosed -= OnDocumentClosed;
        _recordingShown?.Dispose();
        _sharingShown?.Dispose();
        _trainingShown?.Dispose();
        _pending.Clear();
    }

    // ---- helpers ----

    private string Root(LearningSettings settings) =>
        Path.GetFullPath(settings.Folder.Length > 0 ? settings.Folder
            : Path.Combine(_host.DataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Craft"), "learning"));

    // The model trained: the setting, else the AI plug-in's model; null for none.
    private string? ModelName(LearningSettings settings)
    {
        if (settings.Model.Length > 0)
        {
            return settings.Model;
        }

        try
        {
            string model = _host.Settings.Get("ai", "model").Trim();
            return model.Length > 0 ? model : null;
        }
        catch (Exception error) when (error is NotRegisteredException or ArgumentException)
        {
            return null;                                               // no AI plug-in
        }
    }

    // File work off the UI thread, one at a time.
    private Task Run(Action action) => Task.Run(() =>
    {
        lock (_io)
        {
            action();
        }
    });

    private Task<T> Run<T>(Func<T> work) => Task.Run(() =>
    {
        lock (_io)
        {
            return work();
        }
    });

    private static string Relative(string? workspace, string path)
    {
        if (workspace is not null)
        {
            string relative = Path.GetRelativePath(workspace, path);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            {
                return relative.Replace('\\', '/');
            }
        }

        return Path.GetFileName(path);
    }

    // How far `kept` is from `answer`: their edit distance over the longer length (0 the same, 1 nothing in common).
    internal static double Distance(string answer, string kept)
    {
        if (answer.Length == 0 && kept.Length == 0)
        {
            return 0;
        }

        int prefix = 0;
        while (prefix < answer.Length && prefix < kept.Length && answer[prefix] == kept[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < answer.Length - prefix && suffix < kept.Length - prefix && answer[^(suffix + 1)] == kept[^(suffix + 1)])
        {
            suffix++;
        }

        var a = answer.AsSpan(prefix, answer.Length - prefix - suffix);
        var b = kept.AsSpan(prefix, kept.Length - prefix - suffix);
        int longer = Math.Max(answer.Length, kept.Length);
        if (a.Length > 2000 || b.Length > 2000)
        {
            return Math.Min(1, (double)Math.Max(a.Length, b.Length) / longer);   // too long to compare: what differs
        }

        var row = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            row[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            int diagonal = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int above = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }

        return (double)row[b.Length] / longer;
    }

    private enum PendingState
    {
        Proposed,
        Applied,
        Typed,
    }

    // An answer waiting for its verdict.
    private sealed class Pending(AiAnswer answer)
    {
        public AiAnswer Answer { get; } = answer;

        public string Kind => Answer.Kind;

        public PendingState State { get; set; }

        public ITextBuffer? Buffer { get; set; }

        public int Start { get; set; }

        public int Length { get; set; }

        public bool Edited { get; set; }

        public bool Undone { get; set; }

        public string? Verdict { get; set; }

        public string? Kept { get; set; }

        public DateTimeOffset Touched { get; set; } = DateTimeOffset.UtcNow;
    }

    // Progress reports straight to the callback (Progress<T> posts to a captured context, which the thread pool has not).
    private sealed class Progress(Action<TrainingProgress> report) : IProgress<TrainingProgress>
    {
        public void Report(TrainingProgress value) => report(value);
    }
}
