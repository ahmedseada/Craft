// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>Where an inline chat is (X part 9 item 23).</summary>
public enum InlineChatState
{
    /// <summary>Not open.</summary>
    Closed,

    /// <summary>The box is open, waiting for what to do.</summary>
    Input,

    /// <summary>Asked; nothing has come back yet.</summary>
    Asking,

    /// <summary>The answer streams in.</summary>
    Streaming,

    /// <summary>An answer is shown as a ghost diff: Accept, Discard, Retry, or a follow-up.</summary>
    Answered,

    /// <summary>No model can answer now: the box says why and offers the load.</summary>
    NeedsModel,

    /// <summary>The model is loading at the user's press; the request goes once it is loaded.</summary>
    Loading,

    /// <summary>The request failed (said why): Retry or Close.</summary>
    Failed,
}

/// <summary>What a row of the ghost diff is.</summary>
public enum InlineChatRowKind
{
    /// <summary>A line the answer keeps.</summary>
    Same,

    /// <summary>A line the answer removes (drawn struck through).</summary>
    Removed,

    /// <summary>A line the answer adds (drawn on the added fill).</summary>
    Added,

    /// <summary>A line the streaming answer has not reached yet.</summary>
    Pending,
}

/// <summary>A row of the ghost diff: what it is and its text (tabs expanded, no line break).</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Text">Its text.</param>
public readonly record struct InlineChatRow(InlineChatRowKind Kind, string Text);

/// <summary>A button of the inline chat's box.</summary>
public enum InlineChatButton
{
    /// <summary>Sends what is typed (Enter).</summary>
    Send,

    /// <summary>Applies the answer as one undo step (Ctrl+Enter).</summary>
    Accept,

    /// <summary>Drops the answer and closes the box; the text stays as it was (Escape).</summary>
    Discard,

    /// <summary>Asks the last instruction again.</summary>
    Retry,

    /// <summary>Stops the answer streaming.</summary>
    Stop,

    /// <summary>Loads the model (only on this press, note 23).</summary>
    Load,

    /// <summary>Closes the box.</summary>
    Close,
}

/// <summary>
/// The inline chat's box and ghost diff as drawn this frame (view coordinates): the box's rectangle, its input and status
/// rows, the first ghost row's top, and its buttons. Worked out once per change of the scroll, the viewport or the chat.
/// </summary>
public sealed class InlineChatLayout
{
    private readonly InlineChatButton[] _buttons = new InlineChatButton[4];
    private readonly float[] _lefts = new float[4];
    private readonly float[] _widths = new float[4];

    /// <summary>The box's left edge.</summary>
    public float Left { get; internal set; }

    /// <summary>The box's top.</summary>
    public float Top { get; internal set; }

    /// <summary>The box's width.</summary>
    public float Width { get; internal set; }

    /// <summary>The box's height.</summary>
    public float Height { get; internal set; }

    /// <summary>The input row's top.</summary>
    public float InputTop { get; internal set; }

    /// <summary>The status row's top (its buttons on it).</summary>
    public float StatusTop { get; internal set; }

    /// <summary>The first ghost row's top (the range's first line).</summary>
    public float RowsTop { get; internal set; }

    /// <summary>The range's height on the screen (its lines as drawn).</summary>
    public float RangeHeight { get; internal set; }

    /// <summary>Where the ghost rows start (the text's left, scrolled).</summary>
    public float RowsLeft { get; internal set; }

    /// <summary>How many buttons the status row holds.</summary>
    public int ButtonCount { get; internal set; }

    /// <summary>The button <paramref name="index"/>.</summary>
    public InlineChatButton ButtonAt(int index) => _buttons[index];

    /// <summary>The button <paramref name="index"/>'s left edge.</summary>
    public float ButtonLeft(int index) => _lefts[index];

    /// <summary>The button <paramref name="index"/>'s width.</summary>
    public float ButtonWidth(int index) => _widths[index];

    internal void Clear() => ButtonCount = 0;

    internal void Add(InlineChatButton button, float left, float width)
    {
        if (ButtonCount < _buttons.Length)
        {
            _buttons[ButtonCount] = button;
            _lefts[ButtonCount] = left;
            _widths[ButtonCount] = width;
            ButtonCount++;
        }
    }
}

/// <summary>
/// The editor's inline chat (X part 9 item 23, Ctrl+I): at the caret (its line) or on a selection (its whole lines) a
/// small box opens inline in the editor, over the selection or under the line; what the user types goes, with the lines,
/// the file's surroundings and its problems, to the first registered <see cref="IInlineChatProvider"/> (the AI plug-in's,
/// the chat role's loaded model; none loaded, the box says so and offers the load, never loading by itself). The answer
/// streams in as a ghost diff in place (added lines on the added fill, removed ones struck), an overlay the text moves
/// under only when the user accepts: Accept (Ctrl+Enter) applies it as one undo step, Discard (Escape) leaves the text
/// as it was, Retry asks again, and a follow-up typed in the box refines the same edit. Up and Down walk the box's
/// history. The provider runs on the thread pool; its pieces come back through the editor's dispatcher. Used on the UI
/// thread.
/// </summary>
public sealed class InlineChatController : IDisposable
{
    private const int HistoryLimit = 50;
    private static readonly List<string> SharedHistory = [];
    private static readonly Lock HistoryGate = new();
    private readonly EditorModel _model;
    private readonly InlineChatLayout _layout = new();
    private readonly List<InlineChatTurn> _turns = [];
    private readonly StringBuilder _answer = new();
    private CancellationTokenSource? _request;
    private int _generation;
    private int _version;
    private int _rowsVersion = -1;
    private InlineChatRow[] _rows = [];
    private int _added;
    private int _removed;
    private string _pending = "";
    private int _historyIndex = -1;
    private string _draft = "";
    private bool _accepting;
    private (float X, float Y, float W, float H, int Version, float Line, float Cell) _layoutKey = (-1, -1, -1, -1, -1, -1, -1);
    private InlineChatPlan? _plan;
    private IInlineChatProvider? _provider;

    internal InlineChatController(EditorModel model) => _model = model;

    /// <summary>Raised on the UI thread when anything shown changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Where it is.</summary>
    public InlineChatState State { get; private set; }

    /// <summary>Whether the box is open.</summary>
    public bool IsOpen => State != InlineChatState.Closed;

    /// <summary>Whether the box has the keyboard (typed text goes into it, not the document).</summary>
    public bool HasFocus { get; private set; }

    /// <summary>What is typed in the box.</summary>
    public string Input { get; private set; } = "";

    /// <summary>The box's caret (an index in <see cref="Input"/>).</summary>
    public int InputCaret { get; private set; }

    /// <summary>Where the lines start in the text now.</summary>
    public int Start { get; private set; }

    /// <summary>Where they end in the text now (after the last line's break).</summary>
    public int End { get; private set; }

    /// <summary>The lines' text when the box opened (what the answer replaces).</summary>
    public string Original { get; private set; } = "";

    /// <summary>Whether the lines were changed (typed in, undone) since the box opened: Accept then says so.</summary>
    public bool RangeChanged { get; private set; }

    /// <summary>Whether the box sits over the lines (a selection) rather than under them (a caret).</summary>
    public bool OverSelection { get; private set; }

    /// <summary>The answer so far (the replacement of the lines).</summary>
    public string Answer => _answer.ToString();

    /// <summary>What the line under the input says (the model, why none, what failed), or null.</summary>
    public string? Message { get; private set; }

    /// <summary>The model answering, or null.</summary>
    public string? Model { get; private set; }

    /// <summary>The instruction of the request shown (or the one waiting for the model's load).</summary>
    public string Instruction { get; private set; } = "";

    /// <summary>The exchanges so far (a follow-up sends them, so it refines the same edit).</summary>
    public IReadOnlyList<InlineChatTurn> Turns => _turns;

    /// <summary>The load button's title when no model can answer (<see cref="InlineChatState.NeedsModel"/>).</summary>
    public string LoadTitle => _plan?.LoadTitle ?? "Load the chat model";

    /// <summary>The requests sent since the editor opened.</summary>
    public long Requests { get; private set; }

    /// <summary>The answers accepted.</summary>
    public long Accepted { get; private set; }

    /// <summary>Pieces that arrived for a request already stopped or replaced (dropped, never shown).</summary>
    public long LatePieces { get; private set; }

    /// <summary>The box's history (instructions sent, newest last), shared by the editors of this process.</summary>
    public static IReadOnlyList<string> History
    {
        get
        {
            lock (HistoryGate)
            {
                return [.. SharedHistory];
            }
        }
    }

    /// <summary>The ghost diff's rows (the original lines against the answer; while streaming, the lines not reached yet are pending).</summary>
    public IReadOnlyList<InlineChatRow> Rows
    {
        get
        {
            EnsureRows();
            return _rows;
        }
    }

    /// <summary>The lines the answer adds.</summary>
    public int AddedLines
    {
        get
        {
            EnsureRows();
            return _added;
        }
    }

    /// <summary>The lines the answer removes.</summary>
    public int RemovedLines
    {
        get
        {
            EnsureRows();
            return _removed;
        }
    }

    /// <summary>The status row's text for the state now.</summary>
    public string StatusText => State switch
    {
        InlineChatState.Input => _turns.Count > 0 ? "Enter refine · Esc discard · ↑↓ history" : "Enter send · Esc close · ↑↓ history",
        InlineChatState.Asking => Model is null ? "Asking…" : $"Asking {Model}…",
        InlineChatState.Streaming => string.Create(CultureInfo.InvariantCulture, $"Writing{(Model is null ? "" : " with " + Model)}… +{AddedLines} −{RemovedLines}"),
        InlineChatState.Answered => string.Create(CultureInfo.InvariantCulture, $"+{AddedLines} −{RemovedLines} · Ctrl+Enter accept · Esc discard · type to refine"),
        InlineChatState.Loading => Message ?? "Loading the model… the request is sent once it is loaded.",
        _ => Message ?? "",
    };

    /// <summary>The input's placeholder (shown while it is empty).</summary>
    public string Placeholder => _turns.Count > 0 ? "Refine the edit, or Ctrl+Enter to accept"
        : OverSelection ? "Ask to edit the selected lines" : "Ask to change this line or write code here";

    /// <summary>
    /// Opens the box at the selection's lines (else the caret's line) with the keyboard in it (Ctrl+I); when it is open
    /// already, gives it the keyboard. False for a read-only text.
    /// </summary>
    public bool Open()
    {
        if (IsOpen)
        {
            HasFocus = true;
            Raise();
            return true;
        }

        if (_model.IsReadOnly)
        {
            return false;
        }

        var snapshot = _model.Snapshot;
        var selection = _model.Selection;
        (Start, End) = LinesOf(snapshot, selection.Start, selection.End);
        Original = snapshot.GetText(Start, End - Start);
        OverSelection = !selection.IsEmpty;
        RangeChanged = false;
        _turns.Clear();
        _answer.Clear();
        Input = "";
        InputCaret = 0;
        Instruction = "";
        Message = null;
        Model = null;
        _plan = null;
        _historyIndex = -1;
        State = InlineChatState.Input;
        HasFocus = true;
        _model.InlineCompletion.Dismiss();
        Raise();
        return true;
    }

    /// <summary>Sends what is typed (Enter): the first request, or a follow-up that refines the answer shown. False when nothing is typed.</summary>
    public bool Submit()
    {
        string text = Input.Trim();
        if (!IsOpen || text.Length == 0)
        {
            return false;
        }

        AddHistory(text);
        Input = "";
        InputCaret = 0;
        _historyIndex = -1;
        return Ask(text);
    }

    /// <summary>Asks the last instruction again (the same history, without its last answer). False when none was asked.</summary>
    public bool Retry()
    {
        if (!IsOpen || Instruction.Length == 0)
        {
            return false;
        }

        if (State is InlineChatState.Answered && _turns.Count > 0 && _turns[^1].Instruction == Instruction)
        {
            _turns.RemoveAt(_turns.Count - 1);              // the answer retried is not sent back
        }

        return Ask(Instruction);
    }

    /// <summary>Stops the answer streaming; what came is kept as the answer (Accept still applies it).</summary>
    public void Stop()
    {
        if (State is not (InlineChatState.Asking or InlineChatState.Streaming))
        {
            return;
        }

        CancelRequest();
        Finish(stopped: true);
    }

    /// <summary>
    /// Applies the answer in place of the lines as one undo step (Ctrl+Enter) and closes the box. False (said so) when there
    /// is no answer, or the lines changed meanwhile.
    /// </summary>
    public bool Accept()
    {
        if (State != InlineChatState.Answered || _answer.Length == 0)
        {
            return false;
        }

        var snapshot = _model.Snapshot;
        if (RangeChanged || End > snapshot.Length || !Same(snapshot, Start, End, Original))
        {
            Message = "The lines changed since the request: Retry asks again with the text as it is.";
            State = InlineChatState.Failed;
            RangeChanged = true;
            Raise();
            return false;
        }

        string replacement = Replacement(Original, _answer.ToString());
        int start = Start;
        int end = End;
        Close();
        _accepting = true;
        try
        {
            var edit = new TextEdit(start, end - start, replacement);
            _model.ApplyEdits(new ReadOnlySpan<TextEdit>(in edit), EditKind.Command, new SelectionState(start, start + replacement.Length));
        }
        finally
        {
            _accepting = false;
        }

        _model.EnsureCaretVisible();
        Accepted++;
        return true;
    }

    /// <summary>Drops the answer, stops a request and closes the box (Escape); the text stays as it was.</summary>
    public void Discard() => Close();

    /// <summary>
    /// Loads the model at the user's press (the box's load button): the box says it loads, and the instruction waiting is
    /// sent once the model is loaded. Nothing loads otherwise.
    /// </summary>
    public async Task LoadAsync()
    {
        if (State != InlineChatState.NeedsModel || _plan?.LoadAsync is not { } load)
        {
            return;
        }

        State = InlineChatState.Loading;
        Message = $"Loading the model… \"{Instruction}\" is sent once it is loaded (its progress is in the status bar).";
        Raise();
        try
        {
            await load(CancellationToken.None).ConfigureAwait(true);
        }
#pragma warning disable CA1031 // A failed load is said in the box; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            if (State == InlineChatState.Loading)
            {
                Fail("The model did not load: " + error.Message);
            }

            return;
        }

        if (State == InlineChatState.Loading)
        {
            Ask(Instruction);
        }
    }

    /// <summary>Gives the keyboard back to the text (a click in the text); the box stays.</summary>
    public void Blur()
    {
        if (HasFocus)
        {
            HasFocus = false;
            Raise();
        }
    }

    /// <summary>
    /// The box and the ghost diff's places in the view now (worked out once per change of the scroll, the viewport or the
    /// chat); null when the box is closed.
    /// </summary>
    public InlineChatLayout? Layout()
    {
        if (!IsOpen)
        {
            return null;
        }

        var metrics = _model.Metrics;
        float line = metrics.LineHeight;
        float cell = metrics.CellWidth;
        var key = (_model.ScrollX, _model.ScrollY, _model.ViewportWidth, _model.ViewportHeight, _version, line, cell);
        if (key == _layoutKey)
        {
            return _layout;
        }

        _layoutKey = key;
        var snapshot = _model.Snapshot;
        int firstLine = snapshot.GetLineIndex(Math.Clamp(Start, 0, snapshot.Length));
        int lastLine = LastLine(snapshot);
        float rangeTop = _model.PointFromPosition(snapshot.GetLine(firstLine).Start).Y - _model.ScrollY;
        float rangeBottom = _model.PointFromPosition(snapshot.GetLine(lastLine).Start).Y - _model.ScrollY + line;
        int rows = State is InlineChatState.Streaming or InlineChatState.Answered || (_answer.Length > 0 && State is not InlineChatState.Asking) ? Rows.Count : 0;
        float diffBottom = Math.Max(rangeBottom, rangeTop + (rows * line));
        float height = (2 * line) + 12;
        float left = _model.TextLeft + cell;
        float room = _model.ViewportWidth - left - (2 * cell);
        float width = Math.Max(Math.Min(room, 96 * cell), Math.Min(room, 40 * cell));
        float top = OverSelection && rangeTop - height - 4 >= 0 ? rangeTop - height - 4 : diffBottom + 4;
        _layout.Left = left;
        _layout.Top = top;
        _layout.Width = Math.Max(width, 20 * cell);
        _layout.Height = height;
        _layout.InputTop = top + 4;
        _layout.StatusTop = top + 8 + line;
        _layout.RowsTop = rangeTop;
        _layout.RangeHeight = rangeBottom - rangeTop;
        _layout.RowsLeft = _model.TextLeft - _model.ScrollX;
        _layout.Clear();
        float right = left + _layout.Width - cell;
        foreach (var button in ButtonsOf(State))
        {
            float buttonWidth = (TitleOf(button).Length + 2) * cell;
            right -= buttonWidth;
            _layout.Add(button, right, buttonWidth);
            right -= cell;
        }

        return _layout;
    }

    /// <summary>The title a button shows ("Accept", "Load the chat model").</summary>
    public string TitleOf(InlineChatButton button) => button switch
    {
        InlineChatButton.Send => "Send",
        InlineChatButton.Accept => "Accept",
        InlineChatButton.Discard => "Discard",
        InlineChatButton.Retry => "Retry",
        InlineChatButton.Stop => "Stop",
        InlineChatButton.Load => LoadTitle,
        _ => "Close",
    };

    /// <summary>Presses <paramref name="button"/> (as a click on it does).</summary>
    public void Press(InlineChatButton button)
    {
        switch (button)
        {
            case InlineChatButton.Send:
                Submit();
                break;
            case InlineChatButton.Accept:
                Accept();
                break;
            case InlineChatButton.Retry:
                Retry();
                break;
            case InlineChatButton.Stop:
                Stop();
                break;
            case InlineChatButton.Load:
                _ = LoadAsync();
                break;
            default:
                Close();
                break;
        }
    }

    /// <inheritdoc />
    public void Dispose() => CancelRequest();

    // ---- the editor's input while the box is open ----

    /// <summary>A key while the box has the keyboard: true when the box took it (keys it does not know with Ctrl go on to the editor: save, undo).</summary>
    internal bool HandleKey(EditorKey key, KeyModifiers modifiers)
    {
        if (!IsOpen || !HasFocus)
        {
            return false;
        }

        bool control = (modifiers & KeyModifiers.Control) != 0;
        switch (key)
        {
            case EditorKey.Enter when control:
                if (State == InlineChatState.Answered)
                {
                    Accept();
                }

                return true;
            case EditorKey.Enter:
                Submit();
                return true;
            case EditorKey.Escape:
                Discard();
                return true;
            case EditorKey.Up:
                Recall(-1);
                return true;
            case EditorKey.Down:
                Recall(1);
                return true;
            case EditorKey.Left:
                MoveCaret(control ? -WordBefore() : -1);
                return true;
            case EditorKey.Right:
                MoveCaret(control ? WordAfter() : 1);
                return true;
            case EditorKey.Home:
                MoveCaret(-InputCaret);
                return true;
            case EditorKey.End:
                MoveCaret(Input.Length - InputCaret);
                return true;
            case EditorKey.Backspace when InputCaret > 0:
                int back = control ? WordBefore() : 1;
                Input = Input.Remove(InputCaret - back, back);
                InputCaret -= back;
                Raise();
                return true;
            case EditorKey.Delete when InputCaret < Input.Length:
                int forward = control ? WordAfter() : 1;
                Input = Input.Remove(InputCaret, forward);
                Raise();
                return true;
            case EditorKey.Backspace or EditorKey.Delete or EditorKey.Tab or EditorKey.PageUp or EditorKey.PageDown:
                return true;
            case EditorKey.A when control:
                MoveCaret(Input.Length - InputCaret);
                return true;
            default:
                return !control;                                 // a plain key belongs to the box; a Ctrl chord to the editor
        }
    }

    /// <summary>Typed text while the box has the keyboard (a line break is a space: the box is one line).</summary>
    internal bool Type(string text)
    {
        if (!IsOpen || !HasFocus)
        {
            return false;
        }

        string typed = text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ');
        Input = Input.Insert(InputCaret, typed);
        InputCaret += typed.Length;
        _historyIndex = -1;
        Raise();
        return true;
    }

    /// <summary>A press of the primary button: on the box (a button pressed, or the keyboard to the box) true; elsewhere the box loses the keyboard and false.</summary>
    internal bool PointerPressed(float viewX, float viewY)
    {
        if (Layout() is not { } layout)
        {
            return false;
        }

        if (viewX < layout.Left || viewX > layout.Left + layout.Width || viewY < layout.Top || viewY > layout.Top + layout.Height)
        {
            Blur();
            return false;
        }

        float line = _model.Metrics.LineHeight;
        if (viewY >= layout.StatusTop && viewY <= layout.StatusTop + line)
        {
            for (int i = 0; i < layout.ButtonCount; i++)
            {
                if (viewX >= layout.ButtonLeft(i) && viewX <= layout.ButtonLeft(i) + layout.ButtonWidth(i))
                {
                    HasFocus = true;
                    Press(layout.ButtonAt(i));
                    return true;
                }
            }
        }

        HasFocus = true;
        Raise();
        return true;
    }

    /// <summary>The text changed: the lines follow it; a change inside them (not the box's own accept) is noted.</summary>
    internal void OnBufferChanged(TextChangedEventArgs change)
    {
        if (!IsOpen || _accepting)
        {
            return;
        }

        foreach (var edit in change.Edits)
        {
            if (edit.Start < End && edit.End > Start || (edit.Length == 0 && edit.Start > Start && edit.Start < End))
            {
                RangeChanged = true;
            }
        }

        int start = EditorModel.MapPosition(Start, change.Edits);
        int end = EditorModel.MapPosition(End, change.Edits);
        Start = Math.Min(start, end);
        End = Math.Max(start, end);
        Raise();
    }

    // ---- the request ----

    private bool Ask(string instruction)
    {
        CancelRequest();
        Instruction = instruction;
        _answer.Clear();
        _pending = "";
        _rowsVersion = -1;
        Message = null;
        var provider = InlineChatProviders.First;
        if (provider is null)
        {
            Fail("Inline chat needs a plug-in that answers it (the AI plug-in, Craft.Ai), and none is loaded.");
            return false;
        }

        InlineChatPlan plan;
        try
        {
            plan = provider.Plan();
        }
#pragma warning disable CA1031 // A provider's failure is said in the box.
        catch (Exception error)
#pragma warning restore CA1031
        {
            Fail(error.Message);
            return false;
        }

        if (plan.Model is null)
        {
            _plan = plan;
            Message = plan.Why ?? "No model can answer now.";
            State = plan.LoadAsync is null ? InlineChatState.Failed : InlineChatState.NeedsModel;
            Raise();
            return false;
        }

        if (RangeChanged)
        {
            // The lines as they are now are what the model rewrites (the earlier answers were about the old text).
            var now = _model.Snapshot;
            (Start, End) = LinesOf(now, Math.Clamp(Start, 0, now.Length), Math.Clamp(End, 0, now.Length));
            Original = now.GetText(Start, End - Start);
            RangeChanged = false;
            _turns.Clear();
        }

        _provider = provider;
        _plan = plan;
        Model = plan.Model;
        var snapshot = _model.Snapshot;
        var request = new InlineChatRequest(snapshot, Start, End, instruction, _model.Document.FilePath, _model.Language?.Name ?? _model.LanguageName)
        {
            Diagnostics = DiagnosticsOn(Start, End),
            History = [.. _turns],
            WorkspaceFolder = _model.WorkspaceRoot,
        };
        int generation = ++_generation;
        var source = new CancellationTokenSource();
        _request = source;
        var token = source.Token;
        State = InlineChatState.Asking;
        Requests++;
        Raise();
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var piece in provider.EditAsync(request, token).ConfigureAwait(false))
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    _model.Post(() => OnPiece(generation, piece));
                }

                _model.Post(() => OnEnded(generation, null));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
#pragma warning disable CA1031 // A provider's failure is said in the box; the editor goes on.
            catch (Exception error)
#pragma warning restore CA1031
            {
                _model.Post(() => OnEnded(generation, error));
            }
        }, CancellationToken.None);
        return true;
    }

    private void OnPiece(int generation, string piece)
    {
        if (generation != _generation || _request is null)
        {
            LatePieces++;
            return;
        }

        _answer.Append(piece);
        State = InlineChatState.Streaming;
        Raise();
    }

    private void OnEnded(int generation, Exception? error)
    {
        if (generation != _generation)
        {
            return;
        }

        _request?.Dispose();
        _request = null;
        if (error is not null)
        {
            Fail(error is InvalidOperationException ? error.Message : $"The request failed: {error.Message}");
            return;
        }

        Finish(stopped: false);
    }

    private void Finish(bool stopped)
    {
        string answer = Clean(_answer.ToString());
        _answer.Clear().Append(answer);
        _rowsVersion = -1;
        if (answer.Length == 0)
        {
            Fail(stopped ? "Stopped before the model wrote anything." : "The model wrote nothing for these lines: Retry, or say it another way.");
            return;
        }

        _turns.Add(new InlineChatTurn(Instruction, answer));
        Message = stopped ? "Stopped: what was written is the answer." : null;
        State = InlineChatState.Answered;
        HasFocus = true;
        Raise();
    }

    private void Fail(string why)
    {
        CancelRequest();
        Message = why;
        State = InlineChatState.Failed;
        Raise();
    }

    private void Close()
    {
        CancelRequest();
        State = InlineChatState.Closed;
        HasFocus = false;
        _answer.Clear();
        _rowsVersion = -1;
        _turns.Clear();
        Input = "";
        InputCaret = 0;
        Message = null;
        Raise();
    }

    private void CancelRequest()
    {
        if (_request is { } request)
        {
            _request = null;
            _generation++;
            request.Cancel();
        }
    }

    private List<EditorDiagnostic> DiagnosticsOn(int start, int end)
    {
        var found = new List<EditorDiagnostic>();
        foreach (var diagnostic in _model.Diagnostics)
        {
            if (diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning && diagnostic.Start <= end && diagnostic.End >= start)
            {
                found.Add(diagnostic);
            }
        }

        return found;
    }

    // ---- the ghost diff ----

    private void EnsureRows()
    {
        if (_rowsVersion == _version)
        {
            return;
        }

        _rowsVersion = _version;
        string answer = _answer.ToString();
        bool streaming = State is InlineChatState.Asking or InlineChatState.Streaming;
        int complete = streaming ? answer.LastIndexOf('\n') + 1 : answer.Length;
        _pending = streaming ? answer[complete..] : "";
        var rows = DiffRows(Original, answer[..complete], streaming, _model.Options.TabSize, out _added, out _removed);
        if (_pending.Length > 0)
        {
            rows.Add(new InlineChatRow(InlineChatRowKind.Added, Expand(_pending, _model.Options.TabSize)));
            _added++;
        }

        _rows = [.. rows];
    }

    /// <summary>
    /// The rows of the ghost diff of <paramref name="original"/> against <paramref name="answer"/>: every line kept, removed
    /// or added, in order; while <paramref name="streaming"/>, the removed lines after the last line the answer reached are
    /// pending (not reached yet) rather than removed.
    /// </summary>
    public static List<InlineChatRow> DiffRows(string original, string answer, bool streaming, int tabSize, out int added, out int removed)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(answer);
        var oldLines = Lines(original);
        var rows = new List<InlineChatRow>(oldLines.Length + 8);
        int context = oldLines.Length + Lines(answer).Length + 1;
        var diff = TextDiff.OfTexts(Normalize(original), Normalize(answer), context, 4000);
        int next = 1;
        foreach (var hunk in diff.Hunks)
        {
            int firstOld = int.MaxValue;
            foreach (var line in hunk.Lines)
            {
                if (line.Kind != DiffLineKind.Added)
                {
                    firstOld = line.OldLine;
                    break;
                }
            }

            int upTo = firstOld == int.MaxValue ? hunk.OldStart : firstOld - 1;
            for (; next <= upTo && next <= oldLines.Length; next++)
            {
                rows.Add(new InlineChatRow(InlineChatRowKind.Same, Expand(oldLines[next - 1], tabSize)));
            }

            foreach (var line in hunk.Lines)
            {
                var kind = line.Kind switch
                {
                    DiffLineKind.Added => InlineChatRowKind.Added,
                    DiffLineKind.Removed => InlineChatRowKind.Removed,
                    _ => InlineChatRowKind.Same,
                };
                rows.Add(new InlineChatRow(kind, Expand(line.Text, tabSize)));
                if (line.Kind != DiffLineKind.Added)
                {
                    next = line.OldLine + 1;
                }
            }
        }

        for (; next <= oldLines.Length; next++)
        {
            rows.Add(new InlineChatRow(InlineChatRowKind.Same, Expand(oldLines[next - 1], tabSize)));
        }

        if (streaming)
        {
            // The trailing removed lines are the ones the answer has not reached yet.
            for (int i = rows.Count - 1; i >= 0 && rows[i].Kind == InlineChatRowKind.Removed; i--)
            {
                rows[i] = rows[i] with { Kind = InlineChatRowKind.Pending };
            }
        }

        added = 0;
        removed = 0;
        foreach (var row in rows)
        {
            added += row.Kind == InlineChatRowKind.Added ? 1 : 0;
            removed += row.Kind == InlineChatRowKind.Removed ? 1 : 0;
        }

        return rows;
    }

    /// <summary>
    /// The text that replaces <paramref name="original"/> for <paramref name="answer"/>: the original's line breaks
    /// ("\r\n" when it has them), and a last break when the original ends with one.
    /// </summary>
    public static string Replacement(string original, string answer)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(answer);
        string text = Normalize(answer);
        if (original.EndsWith('\n') && !text.EndsWith('\n'))
        {
            text += "\n";
        }

        return original.Contains("\r\n", StringComparison.Ordinal) ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;
    }

    /// <summary>
    /// A model's answer as the lines' replacement: the code of its first fenced block when it wrote one (any prose around
    /// it left out), else the answer with its blank lines at both ends removed.
    /// </summary>
    public static string Clean(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        string text = Normalize(answer);
        int fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0 && (fence == 0 || text[fence - 1] == '\n'))
        {
            int bodyStart = text.IndexOf('\n', fence);
            if (bodyStart >= 0)
            {
                int close = text.IndexOf("\n```", bodyStart, StringComparison.Ordinal);
                text = close < 0 ? text[(bodyStart + 1)..] : text[(bodyStart + 1)..(close + 1)];
            }
        }

        int startAt = 0;
        while (startAt < text.Length)
        {
            int newline = text.IndexOf('\n', startAt);
            if (newline < 0 || text.AsSpan(startAt, newline - startAt).Trim().Length > 0)
            {
                break;
            }

            startAt = newline + 1;
        }

        return text[startAt..].TrimEnd('\n', ' ', '\t') is { Length: > 0 } trimmed ? trimmed + "\n" : "";
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string[] Lines(string text)
    {
        string normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return [];
        }

        var lines = normalized.Split('\n');
        return normalized.EndsWith('\n') ? lines[..^1] : lines;
    }

    private static string Expand(string line, int tabSize) =>
        line.Contains('\t', StringComparison.Ordinal) ? line.Replace("\t", new string(' ', Math.Max(1, tabSize)), StringComparison.Ordinal) : line;

    private static bool Same(ITextSnapshot snapshot, int start, int end, string text) =>
        end - start == text.Length && snapshot.GetText(start, end - start) == text;

    // Whole lines from start's line to end's (an end at a line's start leaves that line out), the last line's break with them.
    private static (int Start, int End) LinesOf(ITextSnapshot snapshot, int start, int end)
    {
        start = Math.Clamp(start, 0, snapshot.Length);
        end = Math.Clamp(end, start, snapshot.Length);
        var first = snapshot.GetLine(snapshot.GetLineIndex(start));
        int lastIndex = snapshot.GetLineIndex(end);
        if (end > start && end == snapshot.GetLine(lastIndex).Start && lastIndex > 0)
        {
            lastIndex--;
        }

        return (first.Start, snapshot.GetLine(lastIndex).EndIncludingBreak);
    }

    private int LastLine(ITextSnapshot snapshot)
    {
        int end = Math.Clamp(End, 0, snapshot.Length);
        int line = snapshot.GetLineIndex(end);
        int startLine = snapshot.GetLineIndex(Math.Clamp(Start, 0, snapshot.Length));
        return end > Start && end == snapshot.GetLine(line).Start && line > startLine ? line - 1 : line;
    }

    private static IEnumerable<InlineChatButton> ButtonsOf(InlineChatState state) => state switch
    {
        InlineChatState.Input => [InlineChatButton.Close, InlineChatButton.Send],
        InlineChatState.Asking or InlineChatState.Streaming => [InlineChatButton.Stop],
        InlineChatState.Answered => [InlineChatButton.Retry, InlineChatButton.Discard, InlineChatButton.Accept],
        InlineChatState.NeedsModel => [InlineChatButton.Close, InlineChatButton.Load],
        InlineChatState.Loading => [InlineChatButton.Close],
        _ => [InlineChatButton.Close, InlineChatButton.Retry],
    };

    private static void AddHistory(string text)
    {
        lock (HistoryGate)
        {
            SharedHistory.Remove(text);
            SharedHistory.Add(text);
            if (SharedHistory.Count > HistoryLimit)
            {
                SharedHistory.RemoveAt(0);
            }
        }
    }

    // Up (-1) and Down (+1) through the history; past the newest, what was being typed comes back.
    private void Recall(int step)
    {
        var history = History;
        if (history.Count == 0)
        {
            return;
        }

        if (_historyIndex < 0)
        {
            if (step > 0)
            {
                return;
            }

            _draft = Input;
            _historyIndex = history.Count;
        }

        int next = _historyIndex + step;
        if (next >= history.Count)
        {
            _historyIndex = -1;
            Input = _draft;
        }
        else
        {
            _historyIndex = Math.Max(0, next);
            Input = history[_historyIndex];
        }

        InputCaret = Input.Length;
        Raise();
    }

    private void MoveCaret(int by)
    {
        int caret = Math.Clamp(InputCaret + by, 0, Input.Length);
        if (caret != InputCaret)
        {
            InputCaret = caret;
            Raise();
        }
    }

    private int WordBefore()
    {
        int i = InputCaret;
        while (i > 0 && Input[i - 1] == ' ')
        {
            i--;
        }

        while (i > 0 && Input[i - 1] != ' ')
        {
            i--;
        }

        return Math.Max(1, InputCaret - i);
    }

    private int WordAfter()
    {
        int i = InputCaret;
        while (i < Input.Length && Input[i] != ' ')
        {
            i++;
        }

        while (i < Input.Length && Input[i] == ' ')
        {
            i++;
        }

        return Math.Max(1, i - InputCaret);
    }

    private void Raise()
    {
        _version++;
        _model.Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
