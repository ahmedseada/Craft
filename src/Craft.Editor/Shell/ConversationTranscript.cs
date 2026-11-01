// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;

namespace Craft.Editor.Shell;

/// <summary>
/// A conversation panel's entries as one read-only text (X part 10, the owner: "I can only copy one message"): the chat's
/// and the agent's transcript shown by Craft's own read-only editor surface (note 26: drag selects across messages,
/// Shift+click extends, Ctrl+A selects the whole conversation; B1's line cache: only the shown lines are laid out and
/// drawn). Each entry is written as its lines once when it changes (the user's message a soft bubble at the right, the
/// model's full width under its model and device, Markdown rendered, code blocks in a fill with their header and actions,
/// long blocks folded, the agent's steps as a timeline with their details opened on demand); a streamed reply replaces
/// only its own lines (one buffer change per dispatch). Copying maps the selection back to the messages: Markdown (the
/// speakers named, code fenced, a whole message as it was written) or plain text, the chrome (speakers, actions,
/// headers) never copied. It follows the newest text unless the user scrolled up ("Jump to latest" then). Used on the UI
/// thread.
/// </summary>
public sealed class ConversationTranscript : IDisposable
{
    private readonly PieceTableBuffer _buffer = new("");
    private readonly ReadOnlyTextDocument _document;
    private readonly List<Message> _messages = [];
    private readonly TranscriptWriter _writer = new();
    private readonly Action<Action> _post;
    private readonly HashSet<long> _shownAll = [];
    private readonly HashSet<long> _toggled = [];
    private readonly TranscriptLanguage _language;
    private readonly TranscriptBackdrop _backdrop;
    private readonly Action _raise;               // kept: a frame reads a code block's colours with it and allocates nothing
    private IConversationPanel? _panel;
    private EditorModel? _model;
    private CraftOptions _options;
    private int _columns = 80;
    private bool _following = true;
    private bool _newBelow;
    private float _lastScrollY;
    private int _hovered = -1;
    private int _written;

    /// <summary>A transcript whose late work (a code block's colours) comes back through <paramref name="post"/> (the UI thread's dispatcher).</summary>
    public ConversationTranscript(Action<Action> post, CraftOptions? options = null)
    {
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _options = options ?? new CraftOptions();
        _document = new ReadOnlyTextDocument(_buffer);
        _language = new TranscriptLanguage(this);
        _backdrop = new TranscriptBackdrop(this);
        _raise = _language.Raise;
        _writer.FoldLines = _options.ChatFoldCodeLines;
    }

    /// <summary>The transcript as a read-only document (its edits throw: the editor refuses them first).</summary>
    public ITextDocument Document => _document;

    /// <summary>The language that colours the transcript (its own spans: speakers, Markdown, actions, code by the code's language).</summary>
    public ILanguage Language => _language;

    /// <summary>The fills behind the lines: the user's bubbles, the code blocks, the streaming caret.</summary>
    public ILineBackdrop Backdrop => _backdrop;

    /// <summary>The panel shown, or null.</summary>
    public IConversationPanel? Panel
    {
        get => _panel;
        set
        {
            if (ReferenceEquals(value, _panel))
            {
                return;
            }

            if (_panel is not null)
            {
                _panel.ConversationChanged -= OnConversationChanged;
            }

            _panel = value;
            _shownAll.Clear();
            _toggled.Clear();
            if (value is not null)
            {
                value.ConversationChanged += OnConversationChanged;
            }

            _following = true;
            _newBelow = false;
            Rebuild();
        }
    }

    /// <summary>The columns the text wraps at (the view's width in cells); a change writes every message again.</summary>
    public int Columns
    {
        get => _columns;
        set
        {
            value = Math.Max(20, value);
            if (value != _columns)
            {
                _columns = value;
                Rebuild();
            }
        }
    }

    /// <summary>The options (the code fold, the metrics, the copy format); a change writes every message again.</summary>
    public CraftOptions Options
    {
        get => _options;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            bool changed = value.ChatFoldCodeLines != _options.ChatFoldCodeLines || value.ChatShowMetrics != _options.ChatShowMetrics;
            _options = value;
            _writer.FoldLines = value.ChatFoldCodeLines;
            if (changed)
            {
                Rebuild();
            }
        }
    }

    /// <summary>The theme's colours the fills are drawn in.</summary>
    public EditorColors Colors { get; set; } = EditorThemes.Get(EditorThemes.Dark).Colors;

    /// <summary>The entries written as lines (one message each).</summary>
    public int MessageCount => _messages.Count;

    /// <summary>How many times a message was written (its lines made): a streamed piece writes one, a resize every one.</summary>
    public int MessagesWritten => _written;

    /// <summary>Whether the view follows the newest text: on at first; off when the user scrolls up, on again at the end or by <see cref="JumpToLatest"/>.</summary>
    public bool Following => _following;

    /// <summary>Whether new text came below while the user read further up: the view shows "Jump to latest".</summary>
    public bool ShowsJump => !_following && _newBelow;

    /// <summary>The entry under the pointer (its actions shown), or -1.</summary>
    public int Hovered => _hovered;

    /// <summary>Raised when <see cref="Following"/> or <see cref="ShowsJump"/> changed.</summary>
    public event EventHandler? FollowChanged;

    /// <summary>Raised when the text was written again (a message changed, the panel or the width changed).</summary>
    public event EventHandler? Changed;

    /// <summary>The editor model's options for a transcript: no margin under the end, so following lands on the last line.</summary>
    public static CraftOptions EditorOptions(CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options with { BottomMarginLines = 0, ScrollMarginColumns = 0 };
    }

    /// <summary>The editor that shows the transcript (null for none): its fills, its pointer and its place are the transcript's.</summary>
    public void Attach(EditorModel? model)
    {
        if (ReferenceEquals(model, _model))
        {
            return;
        }

        if (_model is { } old)
        {
            old.ViewportChanged -= OnViewportChanged;
            old.PointerActivity -= OnPointer;
            old.LineBackdrop = null;
        }

        _model = model;
        if (model is not null)
        {
            model.LineBackdrop = _backdrop;
            model.ViewportChanged += OnViewportChanged;
            model.PointerActivity += OnPointer;
            _lastScrollY = model.ScrollY;
            if (_following)
            {
                ScrollToEnd(model);
            }
        }
    }

    /// <summary>Raised when an action of the text was clicked (the controller runs it).</summary>
    public event EventHandler<TranscriptAction>? ActionClicked;

    /// <summary>Scrolls to the newest text and follows it again.</summary>
    public void JumpToLatest()
    {
        _newBelow = false;
        SetFollowing(true);
        if (_model is { } model)
        {
            ScrollToEnd(model);
        }
    }

    /// <summary>The entry whose message holds <paramref name="position"/>, or -1.</summary>
    public int EntryAt(int position)
    {
        int index = MessageIndexAt(position);
        return index < 0 ? -1 : _messages[index].Entry;
    }

    /// <summary>Where the message of <paramref name="entry"/> lies in the text (start and end), or (-1, -1).</summary>
    public (int Start, int End) RangeOf(int entry)
    {
        foreach (var message in _messages)
        {
            if (message.Entry == entry)
            {
                return (message.Start, message.Start + message.Text.Length);
            }
        }

        return (-1, -1);
    }

    /// <summary>The first line of <paramref name="entry"/>'s message, or -1.</summary>
    public int FirstLineOf(int entry)
    {
        foreach (var message in _messages)
        {
            if (message.Entry == entry)
            {
                return message.FirstLine;
            }
        }

        return -1;
    }

    /// <summary>The action placed at <paramref name="position"/>, or null.</summary>
    public TranscriptAction? ActionAt(int position)
    {
        int index = MessageIndexAt(position);
        if (index < 0)
        {
            return null;
        }

        var message = _messages[index];
        int local = position - message.Start;
        foreach (ref readonly var line in message.Lines.AsSpan())
        {
            if (local < line.Start || local > line.Start + line.Length)
            {
                continue;
            }

            int column = local - line.Start;
            for (int i = line.ActionStart; i < line.ActionStart + line.ActionCount; i++)
            {
                var action = message.Actions[i];
                if (column >= action.Column && column < action.Column + action.Length)
                {
                    return action.Action;
                }
            }

            break;
        }

        return null;
    }

    /// <summary>The kind of the line at <paramref name="line"/> (the document's line index), or null past the end.</summary>
    public TranscriptLineKind? KindOfLine(int line)
    {
        int index = MessageIndexOfLine(line);
        return index < 0 ? null : _messages[index].Lines[line - _messages[index].FirstLine].Kind;
    }

    /// <summary>
    /// The text from <paramref name="start"/> to <paramref name="end"/> as the user copies it: Markdown (each message whole
    /// as it was written when the range holds all of its text, else the text shown with code fenced; the speakers named
    /// when it spans messages) or plain (the text shown, no speakers, no fences). The chrome is never copied.
    /// </summary>
    public string Copy(int start, int end, bool markdown)
    {
        start = Math.Clamp(start, 0, _buffer.Current.Length);
        end = Math.Clamp(end, start, _buffer.Current.Length);
        if (end <= start || _panel is not { } panel)
        {
            return "";
        }

        int first = MessageIndexAt(start);
        int last = MessageIndexAt(end - 1);
        if (first < 0 || last < 0)
        {
            return "";
        }

        bool several = last > first;
        var text = new StringBuilder();
        for (int i = first; i <= last; i++)
        {
            var message = _messages[i];
            if (message.Entry >= panel.Entries.Count)
            {
                continue;
            }

            var entry = panel.Entries[message.Entry];
            int s = Math.Max(start, message.Start);
            int e = Math.Min(end, message.Start + message.Text.Length);
            var (contentStart, contentEnd) = ContentRange(message);
            if (contentStart < 0 || e <= contentStart || s >= contentEnd)
            {
                continue;                     // only chrome was selected here
            }

            if (text.Length > 0)
            {
                text.Append("\n\n");
            }

            if (markdown && several && Speaker(panel, message.Entry) is { } speaker)
            {
                text.Append("**").Append(speaker).Append(":**\n\n");
            }

            if (markdown && s <= contentStart && e >= contentEnd)
            {
                text.Append(Source(entry));
            }
            else
            {
                Shown(message, s, e, markdown, text);
            }
        }

        return text.ToString();
    }

    /// <summary>The whole conversation as Markdown under <paramref name="title"/> (Copy conversation, Export conversation).</summary>
    public string ConversationMarkdown(string? title = null)
    {
        string body = _messages.Count == 0 ? "" : Copy(0, _buffer.Current.Length, markdown: true);
        if (_messages.Count == 1 && _panel is { } panel && Speaker(panel, _messages[0].Entry) is { } speaker)
        {
            body = $"**{speaker}:**\n\n{body}";
        }

        return title is { Length: > 0 } ? $"# {title}\n\n{body}\n" : body + "\n";
    }

    /// <summary>The message of <paramref name="entry"/> as Markdown (as written) or plain (as shown).</summary>
    public string MessageText(int entry, bool markdown)
    {
        if (_panel is not { } panel || (uint)entry >= (uint)panel.Entries.Count)
        {
            return "";
        }

        if (markdown)
        {
            return Source(panel.Entries[entry]);
        }

        var (start, end) = RangeOf(entry);
        return start < 0 ? "" : Copy(start, end, markdown: false);
    }

    /// <summary>The message of <paramref name="entry"/> quoted for a reply ("&gt; " before each line, a blank line after).</summary>
    public string Quote(int entry)
    {
        string text = MessageText(entry, markdown: false).TrimEnd();
        if (text.Length == 0)
        {
            return "";
        }

        var quoted = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            quoted.Append("> ").Append(line).Append('\n');
        }

        return quoted.Append('\n').ToString();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Attach(null);
        if (_panel is not null)
        {
            _panel.ConversationChanged -= OnConversationChanged;
            _panel = null;
        }

        foreach (var message in _messages)
        {
            message.Dispose();
        }

        _messages.Clear();
    }

    // ---- writing ----

    private void OnConversationChanged(object? sender, ConversationChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _panel) || _panel is not { } panel)
        {
            return;
        }

        switch (e.Change)
        {
            case ConversationChange.EntryAdded when e.Index == _messages.Count && e.Index < panel.Entries.Count:
                if (e.Index > 0)
                {
                    Replace(e.Index - 1);                  // its header may follow from what came next (a thinking step)
                }

                Append(e.Index);
                break;
            case ConversationChange.EntryUpdated when e.Index >= 0 && e.Index < _messages.Count && e.Index < panel.Entries.Count:
                Replace(e.Index);
                break;
            case ConversationChange.Reset when SameEntries(panel):
                return;                                    // the status, the chips or the busy state: no text changed
            default:
                Rebuild();
                return;
        }

        Updated();
    }

    // A reset that left every entry as it was written (the status line, the chips): nothing to write.
    private bool SameEntries(IConversationPanel panel)
    {
        if (panel.Entries.Count != _messages.Count)
        {
            return false;
        }

        for (int i = 0; i < _messages.Count; i++)
        {
            if (!ReferenceEquals(panel.Entries[i], _messages[i].Source))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Writes every message again (a new panel, a reset, a new width).</summary>
    public void Rebuild()
    {
        var model = _model;
        int keep = model is not null && !_following && _messages.Count > 0
            ? EntryAt(model.Snapshot.GetLine(Math.Clamp((int)(model.ScrollY / Math.Max(1, model.Metrics.LineHeight)), 0, model.Snapshot.LineCount - 1)).Start)
            : -1;
        foreach (var message in _messages)
        {
            message.Dispose();
        }

        _messages.Clear();
        var text = new StringBuilder();
        int line = 0;
        if (_panel is { } panel)
        {
            for (int i = 0; i < panel.Entries.Count; i++)
            {
                var message = Write(panel, i);
                message.Start = text.Length;
                message.FirstLine = line;
                text.Append(message.Text);
                line += message.Lines.Length;
                _messages.Add(message);
            }
        }

        var old = _buffer.Current;
        _buffer.Apply([new TextEdit(0, old.Length, text.ToString())]);
        if (model is not null)
        {
            model.Select(0, 0);
            if (_following)
            {
                ScrollToEnd(model);
            }
            else if (keep >= 0 && FirstLineOf(keep) is >= 0 and var first)
            {
                model.ScrollToOffsets(model.ScrollX, first * model.Metrics.LineHeight, instant: true);
            }
        }

        _language.Raise();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Append(int entry)
    {
        var message = Write(_panel!, entry);
        message.Start = _buffer.Current.Length;
        message.FirstLine = _messages.Count == 0 ? 0 : _messages[^1].FirstLine + _messages[^1].Lines.Length;
        _messages.Add(message);
        _buffer.Apply([TextEdit.Insert(message.Start, message.Text)]);
    }

    // The message of `entry` written again and its lines replaced in one change; what comes after moves with it.
    private void Replace(int entry)
    {
        var old = _messages[entry];
        var message = Write(_panel!, entry);
        message.Start = old.Start;
        message.FirstLine = old.FirstLine;
        int delta = message.Text.Length - old.Text.Length;
        int lines = message.Lines.Length - old.Lines.Length;
        old.Dispose();
        _messages[entry] = message;
        for (int i = entry + 1; i < _messages.Count; i++)
        {
            _messages[i].Start += delta;
            _messages[i].FirstLine += lines;
        }

        if (string.Equals(old.Text, message.Text, StringComparison.Ordinal))
        {
            _language.Raise();                          // the same text, other spans (a step's state): colours only
            return;
        }

        var selection = _model?.Selection ?? default;
        int oldEnd = old.Start + old.Text.Length;
        _buffer.Apply([new TextEdit(old.Start, old.Text.Length, message.Text)]);
        if (_model is { } model && !selection.IsEmpty)
        {
            model.Select(Map(selection.Anchor), Map(selection.Caret));
        }

        int Map(int position) => position < old.Start ? position : position >= oldEnd ? position + delta : Math.Min(position, old.Start + message.Text.Length);
    }

    // After a message was added or replaced: follow the end, or say there is more below.
    private void Updated()
    {
        if (_model is { } model)
        {
            if (_following)
            {
                ScrollToEnd(model);
            }
            else if (!_newBelow)
            {
                _newBelow = true;
                FollowChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        _language.Raise();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Message Write(IConversationPanel panel, int index)
    {
        _written++;
        var entry = panel.Entries[index];
        var previous = index > 0 ? panel.Entries[index - 1] : null;
        var next = index + 1 < panel.Entries.Count ? panel.Entries[index + 1] : null;
        var w = _writer;
        w.Clear();
        w.Columns = _columns;
        bool timeline = panel.ShowsTimeline;
        bool colour = !entry.IsStreaming;
        int caretLine = -1;
        int caretColumn = 0;
        var copy = new TranscriptAction(TranscriptActionKind.CopyMessage, index);
        var menu = new TranscriptAction(TranscriptActionKind.MessageMenu, index);
        switch (entry.Role)
        {
            case ConversationRole.User:
            {
                var actions = new List<(string, TranscriptAction, bool)> { ("Copy", copy, false) };
                if ((panel.Capabilities & ConversationCapabilities.EditAndResend) != 0)
                {
                    actions.Add(("Edit", new TranscriptAction(TranscriptActionKind.Edit, index), false));
                }

                actions.Add(("⋯", menu, false));
                w.LabelAndActions(TranscriptLineKind.Meta, "You", ChatClasses.Quiet, actions, ChatClasses.ActionIdle);
                Bubble(w, entry.Text);
                if (entry.Detail is { Length: > 0 } detail)
                {
                    w.Line(TranscriptLineKind.Footer, detail, Math.Max(0, _columns - detail.Length - 1), ChatClasses.Quiet);
                }

                break;
            }

            case ConversationRole.Assistant when timeline:
            {
                long key = Key(index, 0);
                bool thinking = entry.IsStreaming || next is { Role: ConversationRole.Tool or ConversationRole.Approval or ConversationRole.Assistant };
                bool open = Open(key, byDefault: true);
                string title = (open ? "▾ " : "▸ ") + (thinking ? "Thinking" : "Answer") + (entry.Header is { Length: > 0 } h ? " · " + h : "");
                w.LabelAndActions(TranscriptLineKind.Step, title, ChatClasses.Quiet, [("Copy", copy, false), ("⋯", menu, false)], ChatClasses.ActionIdle);
                AddToggle(w, index, key, 2);
                if (open)
                {
                    w.Markdown(index, entry.Text, colour, IsShownAll);
                }
                else
                {
                    w.Line(TranscriptLineKind.Body, FirstLine(entry.Text, _columns - 4), 2, ChatClasses.Quiet);
                }

                break;
            }

            case ConversationRole.Assistant:
            {
                if (previous is null or { Role: ConversationRole.User or ConversationRole.Note or ConversationRole.Error })
                {
                    w.LabelAndActions(TranscriptLineKind.Meta, entry.Header is { Length: > 0 } header ? header : "Assistant", ChatClasses.Quiet,
                        [("Copy", copy, false), ("⋯", menu, false)], ChatClasses.ActionIdle);
                }

                w.Markdown(index, entry.Text, colour, IsShownAll);
                break;
            }

            case ConversationRole.Code:
                w.Code(index, 0, entry.Text, entry.Language, entry.Actions, colour, IsShownAll(Key(index, 0)));
                break;
            case ConversationRole.Tool:
                Step(w, index, entry, entry.Succeeded switch { true => "✓", false => "✗", null => "⋯" },
                    entry.Succeeded switch { true => ChatClasses.Success, false => ChatClasses.Error, null => ChatClasses.Quiet }, timeline);
                break;
            case ConversationRole.Approval:
                Step(w, index, entry with { Text = "Waiting for you: " + entry.Text }, "●", ChatClasses.Waiting, timeline, detailsOpen: true);
                break;
            case ConversationRole.Section:
                w.LabelAndActions(TranscriptLineKind.Step, entry.Text, ChatClasses.Bold, ActionsOf(entry, index));
                break;
            case ConversationRole.File:
            {
                string counts = entry.Detail ?? "";
                string left = "  " + entry.Text + (counts.Length > 0 ? "  " + counts : "");
                w.LabelAndActions(TranscriptLineKind.Step, left, null, ActionsOf(entry, index));
                CountSpans(w, 2 + entry.Text.Length + 2, counts);
                break;
            }

            case ConversationRole.Error:
                w.Wrapped(TranscriptLineKind.Body, entry.Text, null, 0, "", ChatClasses.Error);
                if (entry.Actions.Count > 0)
                {
                    w.LabelAndActions(TranscriptLineKind.Actions, "", null, ActionsOf(entry, index), alignLeft: true);
                }

                break;
            default:
                w.Wrapped(TranscriptLineKind.Body, entry.Text, null, 0, "", ChatClasses.Quiet);
                if (entry.Actions.Count > 0)
                {
                    w.LabelAndActions(TranscriptLineKind.Actions, "", null, ActionsOf(entry, index), alignLeft: true);
                }

                break;
        }

        if (entry.IsStreaming && w.Lines.Count > 0)
        {
            caretLine = w.Lines.Count - 1;
            caretColumn = w.Lines[^1].Length;
        }

        if (!entry.IsStreaming && _options.ChatShowMetrics && entry.Metrics is { Length: > 0 } metrics)
        {
            w.Line(TranscriptLineKind.Footer, metrics, 0, ChatClasses.Quiet);
        }

        // The gap after a message, except between a reply's own parts and between the steps of a timeline.
        bool joined = next is not null && entry.Role is not ConversationRole.User
                      && (next.Role is ConversationRole.Code or ConversationRole.Tool or ConversationRole.Approval or ConversationRole.File
                          || (entry.Role is ConversationRole.Code && next.Role is ConversationRole.Assistant)
                          || (timeline && next.Role is not ConversationRole.User));
        if (!joined)
        {
            w.Line(TranscriptLineKind.Gap, "");
        }

        return new Message(entry, index, w, caretLine, caretColumn);
    }

    private void Bubble(TranscriptWriter w, string text)
    {
        int most = Math.Max(12, (int)(_columns * 0.8) - 2);
        int longest = 0;
        foreach (var line in text.Split('\n'))
        {
            longest = Math.Max(longest, line.TrimEnd('\r').Length);
        }

        int width = Math.Min(most, Math.Max(1, longest));
        int left = Math.Max(0, _columns - width - 3);
        foreach (var raw in text.Split('\n'))
        {
            int before = w.Lines.Count;
            w.Wrapped(TranscriptLineKind.Bubble, raw.TrimEnd('\r'), null, left + 1, width: left + 1 + width);
            for (int i = before; i < w.Lines.Count; i++)
            {
                var lines = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(w.Lines);
                lines[i].BoxLeft = left;
                lines[i].BoxWidth = width + 2;
            }
        }
    }

    // A step of the timeline (or a tool row in the chat): its mark, its name and summary, a toggle when it has details,
    // its details when open, its actions.
    private void Step(TranscriptWriter w, int index, ConversationEntry entry, string mark, string markClass, bool timeline, bool detailsOpen = false)
    {
        long key = Key(index, 1);
        bool hasDetails = entry.Details is { Length: > 0 };
        bool open = hasDetails && Open(key, detailsOpen);
        string toggle = hasDetails ? (open ? "▾ " : "▸ ") : "  ";
        string summary = entry.Detail is { Length: > 0 } detail ? $"{entry.Text}  {detail}" : entry.Text;
        int room = Math.Max(8, _columns - 6);
        string shown = summary.Length <= room ? summary : string.Concat(summary.AsSpan(0, room - 1), "…");
        w.Line(TranscriptLineKind.Step, $"{toggle}{mark} {shown}");
        w.Span(2, mark.Length, markClass);
        w.Span(3 + mark.Length, entry.Text.Length > shown.Length ? shown.Length : entry.Text.Length, entry.Role == ConversationRole.Approval ? ChatClasses.Waiting : ChatClasses.Bold);
        if (entry.Detail is { Length: > 0 } && shown.Length > entry.Text.Length + 2)
        {
            w.Span(3 + mark.Length + entry.Text.Length + 2, shown.Length - entry.Text.Length - 2, ChatClasses.Quiet);
        }

        if (hasDetails)
        {
            w.Action(0, 1, new TranscriptAction(TranscriptActionKind.Toggle, index) { Key = key }, ChatClasses.Quiet);
        }

        if (open)
        {
            foreach (var line in entry.Details!.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
            {
                w.Wrapped(TranscriptLineKind.StepDetail, line, null, 4, "", ChatClasses.Quiet);
            }
        }

        if (entry.Actions.Count > 0)
        {
            w.LabelAndActions(TranscriptLineKind.Actions, "", null, ActionsOf(entry, index), indent: 4, alignLeft: true);
        }

        _ = timeline;
    }

    private static void AddToggle(TranscriptWriter w, int index, long key, int length) =>
        w.Action(0, length - 1, new TranscriptAction(TranscriptActionKind.Toggle, index) { Key = key }, ChatClasses.Quiet);

    // "+3 −1" in the success's and the error's colours.
    private static void CountSpans(TranscriptWriter w, int column, string counts)
    {
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] is '+' or '−' or '-')
            {
                int end = i + 1;
                while (end < counts.Length && char.IsAsciiDigit(counts[end]))
                {
                    end++;
                }

                if (end > i + 1)
                {
                    w.Span(column + i, end - i, counts[i] == '+' ? ChatClasses.Success : ChatClasses.Error);
                    i = end - 1;
                }
            }
        }
    }

    private static List<(string, TranscriptAction, bool)> ActionsOf(ConversationEntry entry, int index) =>
        [.. entry.Actions.Select(a => (a.Label, new TranscriptAction(TranscriptActionKind.Panel, index) { Action = a }, a.Primary))];

    private static string FirstLine(string text, int width)
    {
        string line = text.TrimStart();
        int end = line.IndexOf('\n', StringComparison.Ordinal);
        line = end >= 0 ? line[..end] + " …" : line;
        return line.Length <= width ? line : string.Concat(line.AsSpan(0, Math.Max(1, width - 1)), "…");
    }

    private static long Key(int entry, int part) => ((long)entry << 16) | (uint)part;

    private bool IsShownAll(long key) => _shownAll.Contains(key);

    private bool Open(long key, bool byDefault) => _toggled.Contains(key) ? !byDefault : byDefault;

    /// <summary>Opens or closes a step (by its action's key), or shows a folded code block whole (or folds it again).</summary>
    public void Toggle(TranscriptAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var set = action.Kind == TranscriptActionKind.ShowAll ? _shownAll : _toggled;
        if (!set.Add(action.Key))
        {
            set.Remove(action.Key);
        }

        if ((uint)action.Entry < (uint)_messages.Count)
        {
            Replace(action.Entry);
            _language.Raise();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---- copying ----

    private static string? Speaker(IConversationPanel panel, int entry)
    {
        var entries = panel.Entries;
        var role = entries[entry].Role;
        if (role == ConversationRole.User)
        {
            return "You";
        }

        bool replyStart = role is ConversationRole.Assistant or ConversationRole.Code or ConversationRole.Tool
                          && (entry == 0 || entries[entry - 1].Role is ConversationRole.User);
        return replyStart ? entries[entry].Header is { Length: > 0 } header ? $"Assistant ({header})" : "Assistant" : null;
    }

    /// <summary>An entry as Markdown, as it was written (a code block fenced with its language).</summary>
    public static string Source(ConversationEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Role switch
        {
            ConversationRole.Code => $"```{entry.Language}\n{entry.Text.TrimEnd('\n', '\r')}\n```",
            ConversationRole.Tool => $"- `{entry.Text}`" + (entry.Detail is { Length: > 0 } detail ? $" {detail}" : ""),
            ConversationRole.Approval => $"- Waiting for approval: `{entry.Text}`",
            ConversationRole.Section => $"**{entry.Text}**",
            ConversationRole.File => $"- {entry.Text}" + (entry.Detail is { Length: > 0 } counts ? $" ({counts})" : ""),
            ConversationRole.Error => $"**Error:** {entry.Text}",
            _ => entry.Text,
        };
    }

    // The first and last position of the message's content lines (its chrome left out), or (-1, -1).
    private static (int Start, int End) ContentRange(Message message)
    {
        int start = -1;
        int end = -1;
        foreach (ref readonly var line in message.Lines.AsSpan())
        {
            if (!IsChrome(line.Kind))
            {
                if (start < 0)
                {
                    start = message.Start + line.Start + line.Indent;
                }

                end = message.Start + line.Start + line.Length;
            }
        }

        return (start, end);
    }

    private static bool IsChrome(TranscriptLineKind kind) =>
        kind is TranscriptLineKind.Meta or TranscriptLineKind.CodeHeader or TranscriptLineKind.Fold or TranscriptLineKind.Footer
            or TranscriptLineKind.Actions or TranscriptLineKind.Gap;

    // The content shown between s and e: each content line's text past its decoration; a wrapped line joined to the one
    // before (a space in text, nothing in code); in Markdown a code block's lines fenced with its language.
    private static void Shown(Message message, int s, int e, bool markdown, StringBuilder text)
    {
        int block = -1;
        bool firstLine = true;
        var lines = message.Lines;
        for (int i = 0; i < lines.Length; i++)
        {
            ref readonly var line = ref lines[i];
            int lineStart = message.Start + line.Start;
            int from = Math.Max(s, lineStart + line.Indent);
            int to = Math.Min(e, lineStart + line.Length);
            if (IsChrome(line.Kind) || lineStart + line.Length < s || lineStart > e || (to < from && line.Length > line.Indent))
            {
                continue;
            }

            if (markdown && line.Block != block)
            {
                if (block >= 0)
                {
                    text.Append("\n```");
                }

                if (line.Block >= 0)
                {
                    if (!firstLine)
                    {
                        text.Append('\n');
                    }

                    text.Append("```").Append(message.Blocks[line.Block].Language);
                    firstLine = false;
                }

                block = line.Block;
            }

            bool continues = line.Block >= 0 ? line.BlockOffset > 0 && message.Blocks[line.Block].Code[line.BlockOffset - 1] != '\n' : line.Continuation;
            if (!firstLine)
            {
                text.Append(continues ? (line.Block >= 0 ? "" : " ") : "\n");
            }

            if (to > from)
            {
                text.Append(message.Text, from - message.Start, to - from);
            }

            firstLine = false;
        }

        if (markdown && block >= 0)
        {
            text.Append("\n```");
        }
    }

    // ---- places ----

    private int MessageIndexAt(int position)
    {
        int lo = 0;
        int hi = _messages.Count - 1;
        if (hi < 0 || position < 0)
        {
            return -1;
        }

        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (_messages[mid].Start <= position)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return position < _messages[lo].Start + _messages[lo].Text.Length ? lo : -1;
    }

    private int MessageIndexOfLine(int line)
    {
        int lo = 0;
        int hi = _messages.Count - 1;
        if (hi < 0 || line < 0)
        {
            return -1;
        }

        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (_messages[mid].FirstLine <= line)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return line < _messages[lo].FirstLine + _messages[lo].Lines.Length ? lo : -1;
    }

    // ---- the view ----

    private void OnPointer(object? sender, EditorPointerEventArgs e)
    {
        switch (e.Kind)
        {
            case EditorPointerKind.Hover:
                SetHovered(EntryAt(e.Position));
                break;
            case EditorPointerKind.Exit:
                SetHovered(-1);
                break;
            case EditorPointerKind.Click when ActionAt(e.Position) is { } action:
                ActionClicked?.Invoke(this, action);
                break;
        }
    }

    /// <summary>The entry under the pointer (its message's actions shown), or -1.</summary>
    public void SetHovered(int entry)
    {
        if (entry != _hovered)
        {
            _hovered = entry;
            _language.Raise();
        }
    }

    private static void ScrollToEnd(EditorModel model) =>
        model.ScrollToOffsets(model.ScrollX, Math.Max(0, model.ContentHeight - model.ViewportHeight), instant: true);

    private void SetFollowing(bool following)
    {
        if (following != _following)
        {
            _following = following;
            if (following)
            {
                _newBelow = false;
            }

            FollowChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // The user's scroll: up stops following, back at the end follows again (a frame repeating an older offset is ignored).
    private void OnViewportChanged(object? sender, EventArgs e)
    {
        if (sender is not EditorModel model)
        {
            return;
        }

        float y = model.ScrollY;
        bool atEnd = y + model.ViewportHeight >= model.ContentHeight - (model.Metrics.LineHeight / 2);
        if (atEnd)
        {
            SetFollowing(true);
        }
        else if (y < _lastScrollY - 0.5f)
        {
            SetFollowing(false);
        }

        _lastScrollY = y;
        int columns = model.Metrics.CellWidth > 0 ? (int)((model.ViewportWidth - model.TextLeft) / model.Metrics.CellWidth) - 1 : 0;
        if (columns >= 20 && columns != _columns)
        {
            Columns = columns;
        }
    }

    // ---- what a frame reads (nothing allocated) ----

    internal void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
    {
        if (!ReferenceEquals(snapshot, _buffer.Current) || _messages.Count == 0)
        {
            return;
        }

        int end = Math.Min(snapshot.Length, start + Math.Max(0, length));
        int firstLine = snapshot.GetLineIndex(Math.Clamp(start, 0, snapshot.Length));
        int lastLine = snapshot.GetLineIndex(end);
        int index = MessageIndexOfLine(firstLine);
        if (index < 0)
        {
            return;
        }

        for (int line = firstLine; line <= lastLine && index < _messages.Count; line++)
        {
            var message = _messages[index];
            while (line >= message.FirstLine + message.Lines.Length)
            {
                if (++index >= _messages.Count)
                {
                    return;
                }

                message = _messages[index];
            }

            ref readonly var row = ref message.Lines[line - message.FirstLine];
            int at = message.Start + row.Start;
            if (row.Block >= 0)
            {
                CodeSpans(message, in row, at, into);
                continue;
            }

            bool hovered = message.Entry == _hovered;
            for (int i = row.SpanStart; i < row.SpanStart + row.SpanCount; i++)
            {
                var span = message.Spans[i];
                into.Add(at + span.Column, span.Length, hovered && ReferenceEquals(span.Class, ChatClasses.ActionIdle) ? ChatClasses.Action : span.Class);
            }
        }
    }

    private void CodeSpans(Message message, in TranscriptLine row, int at, TokenSpanBuffer into)
    {
        var block = message.Blocks[row.Block];
        var spans = block.Spans(_post, _raise);
        int from = row.BlockOffset;
        int to = from + row.Length - row.Indent;
        int lo = 0;
        int hi = spans.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (spans[mid].End <= from)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        for (int i = lo; i < spans.Length && spans[i].Start < to; i++)
        {
            int s = Math.Max(spans[i].Start, from);
            int e = Math.Min(spans[i].End, to);
            if (e > s)
            {
                into.Add(at + row.Indent + (s - from), e - s, spans[i].Class);
            }
        }
    }

    internal void DrawBackdrop(IEditorCanvas canvas, int line, float left, float top, float cell, float height, float viewportWidth)
    {
        int index = MessageIndexOfLine(line);
        if (index < 0)
        {
            return;
        }

        var message = _messages[index];
        int local = line - message.FirstLine;
        ref readonly var row = ref message.Lines[local];
        var colors = Colors;
        if (row.BoxWidth > 0)
        {
            bool bubble = row.Kind == TranscriptLineKind.Bubble;
            uint fill = bubble ? ChatClasses.Mix(colors.Accent, colors.Surface, 0.14) : ChatClasses.Mix(colors.Ground, colors.Surface, 0.7);
            float x = left + (row.BoxLeft * cell);
            float width = bubble ? row.BoxWidth * cell : Math.Max(0, viewportWidth - x - cell);
            canvas.FillRect(x, top, width, height, fill);
            if (row.Kind == TranscriptLineKind.CodeHeader)
            {
                canvas.FillRect(x, top + height - 1, width, 1, colors.Border);
            }
        }

        if (local == message.CaretLine)
        {
            canvas.FillRect(left + ((row.Indent > 0 && row.Length == row.Indent ? row.Indent : message.CaretColumn) * cell) + 1, top + 2, Math.Max(2, cell * 0.55f), height - 4, colors.Accent);
        }
    }

    private sealed class Message : IDisposable
    {
        public Message(ConversationEntry source, int entry, TranscriptWriter w, int caretLine, int caretColumn)
        {
            Source = source;
            Entry = entry;
            Text = w.Text.ToString();
            Lines = Normalize(w);
            Spans = [.. w.Spans];
            Actions = [.. w.Actions];
            Blocks = [.. w.Blocks];
            CaretLine = caretLine;
            CaretColumn = caretColumn;
        }

        public ConversationEntry Source { get; }

        public int Entry { get; }

        public string Text { get; }

        public TranscriptLine[] Lines { get; }

        public LineSpan[] Spans { get; private set; }

        public LineAction[] Actions { get; }

        public ChatCodeBlock[] Blocks { get; }

        public int CaretLine { get; }

        public int CaretColumn { get; }

        public int Start { get; set; }

        public int FirstLine { get; set; }

        // Each line's spans in order and without overlaps (a later span wins where two meet): what the renderer expects.
        private static TranscriptLine[] Normalize(TranscriptWriter w)
        {
            var lines = w.Lines.ToArray();
            var spans = w.Spans;
            var ordered = new List<LineSpan>(spans.Count);
            for (int l = 0; l < lines.Length; l++)
            {
                ref var line = ref lines[l];
                int first = ordered.Count;
                if (line.SpanCount > 0)
                {
                    var classes = new string?[line.Length + 1];
                    for (int i = line.SpanStart; i < line.SpanStart + line.SpanCount; i++)
                    {
                        var span = spans[i];
                        for (int c = Math.Max(0, span.Column); c < Math.Min(line.Length, span.Column + span.Length); c++)
                        {
                            classes[c] = span.Class;
                        }
                    }

                    int run = 0;
                    for (int c = 1; c <= line.Length; c++)
                    {
                        if (c == line.Length || !ReferenceEquals(classes[c], classes[run]))
                        {
                            if (classes[run] is { } cls)
                            {
                                ordered.Add(new LineSpan(run, c - run, cls));
                            }

                            run = c;
                        }
                    }
                }

                line.SpanStart = first;
                line.SpanCount = ordered.Count - first;
            }

            spans.Clear();
            spans.AddRange(ordered);
            return lines;
        }

        public void Dispose()
        {
            foreach (var block in Blocks)
            {
                block.Dispose();
            }
        }
    }

    private sealed class TranscriptBackdrop(ConversationTranscript owner) : ILineBackdrop
    {
        public void DrawLine(IEditorCanvas canvas, int line, float left, float top, float cellWidth, float height, float viewportWidth) =>
            owner.DrawBackdrop(canvas, line, left, top, cellWidth, height, viewportWidth);
    }

    // The transcript's language: its own spans, in no registry (no file, no tool and no picker sees it).
    private sealed class TranscriptLanguage(ConversationTranscript owner) : ILanguage
    {
        private readonly List<Session> _sessions = [];

        public string Name => "craft-conversation";

        public string Summary => "The chat's and the agent's transcript (internal to the shell)";

        public IReadOnlyList<string> Extensions => [];

        public LanguageRules Rules => LanguageRules.Plain;

        public ILanguageSession Open(ITextBuffer buffer)
        {
            var session = new Session(this);
            _sessions.Add(session);
            return session;
        }

        // The spans changed without the text (the pointer's message, a block's colours): the editors draw again.
        public void Raise()
        {
            foreach (var session in _sessions)
            {
                session.Raise();
            }
        }

        public sealed class Session(TranscriptLanguage language) : ILanguageSession
        {
            public event EventHandler? Reclassified;

            public void Raise() => Reclassified?.Invoke(this, EventArgs.Empty);

            public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into) => language.Owner.Classify(snapshot, start, length, into);

            public void Dispose() => language._sessions.Remove(this);
        }

        public ConversationTranscript Owner => owner;
    }
}
