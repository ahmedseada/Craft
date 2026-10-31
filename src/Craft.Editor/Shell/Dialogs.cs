// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>What an action of a Craft dialog does, which decides how it is drawn and which key answers it.</summary>
public enum DialogActionRole
{
    /// <summary>A plain answer (drawn as the design's secondary button).</summary>
    Secondary,

    /// <summary>The main answer (the accent's fill); Enter answers it when the focus is on no other action.</summary>
    Primary,

    /// <summary>An answer that loses something ("Don't save", "Delete"): the error's colour.</summary>
    Danger,

    /// <summary>The way out ("Cancel"): Escape answers it.</summary>
    Cancel,
}

/// <summary>An action (a button, or a row of a list) of a Craft dialog.</summary>
/// <param name="Label">Its text.</param>
/// <param name="Role">What it does.</param>
public sealed record DialogAction(string Label, DialogActionRole Role = DialogActionRole.Secondary);

/// <summary>
/// One question, message or choice for the user, shown by the app as Craft's own dialog (an overlay in the window: the
/// dimmed backdrop, a card with the title, the text, the details and the actions), never the platform's message box (the
/// owner's note 10). Made by <see cref="Message"/>, <see cref="Question"/>, <see cref="Unsaved"/>, <see cref="Pick"/> and
/// <see cref="Prompt"/>, or directly.
/// </summary>
/// <param name="Title">The title.</param>
/// <param name="Text">What it is about (may be empty).</param>
/// <param name="Actions">The actions, in order (the answer is the index of the one taken).</param>
public sealed record DialogRequest(string Title, string Text, IReadOnlyList<DialogAction> Actions)
{
    /// <summary>The facts under the text, one a line, in the code font (a command's program, its folder, its time limit).</summary>
    public IReadOnlyList<string> Details { get; init; } = [];

    /// <summary>
    /// Whether the actions are a list of rows to pick from (a picker: the tabs, the commands, the models) rather than
    /// buttons; the list's own way out is <see cref="CancelIndex"/> (none: Escape answers -1).
    /// </summary>
    public bool IsList { get; init; }

    /// <summary>A text field over the actions (a prompt), with this placeholder; null for none.</summary>
    public string? InputPlaceholder { get; init; }

    /// <summary>The text the field starts with.</summary>
    public string InputText { get; init; } = "";

    /// <summary>The action Enter answers and the focus starts on: the first <see cref="DialogActionRole.Primary"/>, else the first that is not the way out, else 0; -1 when there are none.</summary>
    public int DefaultIndex
    {
        get
        {
            int primary = IndexOf(DialogActionRole.Primary);
            if (primary >= 0)
            {
                return primary;
            }

            for (int i = 0; i < Actions.Count; i++)
            {
                if (Actions[i].Role != DialogActionRole.Cancel)
                {
                    return i;
                }
            }

            return Actions.Count > 0 ? 0 : -1;
        }
    }

    /// <summary>The action Escape (and a dismissal) answers: the first <see cref="DialogActionRole.Cancel"/>, or -1 when there is none.</summary>
    public int CancelIndex => IndexOf(DialogActionRole.Cancel);

    /// <summary>A message with OK.</summary>
    public static DialogRequest Message(string title, string text) => new(title, text, [new DialogAction("OK", DialogActionRole.Primary)]);

    /// <summary>
    /// A plug-in's question (<see cref="IEditorHost.AskAsync"/>): its choices as buttons (the first the primary), or as a
    /// list when there are more than <paramref name="buttons"/>; with a Cancel unless the question's dismissal is one of its
    /// own choices. The answer maps back through <see cref="QuestionAnswer"/>.
    /// </summary>
    public static DialogRequest Question(EditorQuestion question, int buttons = 3)
    {
        ArgumentNullException.ThrowIfNull(question);
        bool list = question.Choices.Count > buttons;
        var actions = new List<DialogAction>(question.Choices.Count + 1);
        for (int i = 0; i < question.Choices.Count; i++)
        {
            actions.Add(new DialogAction(question.Choices[i], i == 0 && !list ? DialogActionRole.Primary : DialogActionRole.Secondary));
        }

        bool dismissedIsChoice = question.Dismissed >= 0 && question.Dismissed < question.Choices.Count;
        if (!dismissedIsChoice)
        {
            actions.Add(new DialogAction("Cancel", DialogActionRole.Cancel));
        }

        return new DialogRequest(question.Title, question.Message, actions) { Details = question.Details, IsList = list };
    }

    /// <summary>The answer to <paramref name="question"/> of a dialog made by <see cref="Question"/>: the choice taken, or the question's dismissal.</summary>
    public static int QuestionAnswer(EditorQuestion question, DialogResult result)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Index >= 0 && result.Index < question.Choices.Count)
        {
            return result.Index;
        }

        // The Cancel action, Escape, or the dialog taken away: a dismissal whose choice is one of the question's own is that
        // choice ("Deny" for a command), as the question asked.
        return question.Dismissed;
    }

    /// <summary>The unsaved-changes question: Save (primary), Don't save, Cancel.</summary>
    public static DialogRequest Unsaved(string title) => new($"Save the changes to {title}?", "Your changes are lost if you don't save them.",
        [new DialogAction("Save", DialogActionRole.Primary), new DialogAction("Don't save", DialogActionRole.Danger), new DialogAction("Cancel", DialogActionRole.Cancel)]);

    /// <summary>What <see cref="Unsaved"/>'s answer means.</summary>
    public static UnsavedChoice UnsavedAnswer(DialogResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Index switch
        {
            0 => UnsavedChoice.Save,
            1 => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel,
        };
    }

    /// <summary>A list to pick one of <paramref name="items"/> from (the answer is the item's index; Escape gives -1).</summary>
    public static DialogRequest Pick(string title, IReadOnlyList<string> items, string text = "")
    {
        ArgumentNullException.ThrowIfNull(items);
        var actions = new DialogAction[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            actions[i] = new DialogAction(items[i]);
        }

        return new DialogRequest(title, text, actions) { IsList = true };
    }

    /// <summary>A text to type (the answer's <see cref="DialogResult.Text"/>), with OK and Cancel.</summary>
    public static DialogRequest Prompt(string title, string text, string placeholder, string ok = "OK", string initial = "") =>
        new(title, text, [new DialogAction(ok, DialogActionRole.Primary), new DialogAction("Cancel", DialogActionRole.Cancel)]) { InputPlaceholder = placeholder, InputText = initial };

    private int IndexOf(DialogActionRole role)
    {
        for (int i = 0; i < Actions.Count; i++)
        {
            if (Actions[i].Role == role)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>The answer of a Craft dialog: the index of the action taken (-1 when none was: Escape with no Cancel, taken away), and the field's text.</summary>
/// <param name="Index">The action's index, or -1.</param>
/// <param name="Text">The text field's text (a prompt), or null.</param>
public sealed record DialogResult(int Index, string? Text = null)
{
    /// <summary>No action taken.</summary>
    public static DialogResult None { get; } = new(-1);
}

/// <summary>
/// The Craft dialogs of a window (the owner's note 10): one shown at a time, the next waiting in order. Asked from any
/// thread (a plug-in's question comes from where it runs); <see cref="Changed"/> is raised through the dispatcher, so the
/// view (Craft.Maui's <c>CraftDialog</c>) updates on the UI thread, where it answers. The behaviour is the view's keys:
/// Escape answers the way out, Enter the focused action (the default one first), Tab and Shift+Tab (and the arrows in a
/// list) move the focus among the actions and wrap, so the focus stays in the dialog; while one shows, every other key is
/// the dialog's (nothing behind it runs). A request's cancellation takes it away with <see cref="DialogResult.None"/>.
/// </summary>
public sealed class DialogQueue : IEditorKeyHandler
{
    private readonly Lock _gate = new();
    private readonly Action<Action> _dispatch;
    private readonly Queue<Pending> _waiting = new();
    private Pending? _current;
    private int _focused = -1;

    /// <summary>A queue raising <see cref="Changed"/> through <paramref name="dispatch"/> (the UI thread's dispatcher; the tests' runs it at once).</summary>
    public DialogQueue(Action<Action> dispatch) => _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));

    /// <summary>The dialog shown, or null.</summary>
    public DialogRequest? Current
    {
        get
        {
            lock (_gate)
            {
                return _current?.Request;
            }
        }
    }

    /// <summary>Whether a dialog shows.</summary>
    public bool IsOpen => Current is not null;

    /// <summary>The dialogs waiting behind the one shown.</summary>
    public int Waiting
    {
        get
        {
            lock (_gate)
            {
                return _waiting.Count;
            }
        }
    }

    /// <summary>The action the focus is on (-1 for none, or the text field when the dialog has one and nothing else took it).</summary>
    public int Focused
    {
        get
        {
            lock (_gate)
            {
                return _focused;
            }
        }
    }

    /// <summary>Raised (through the dispatcher) when the dialog shown changed: one opened, one was answered, the next came.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised (through the dispatcher) when the focus moved to another action (the view focuses its button).</summary>
    public event EventHandler? FocusMoved;

    /// <summary>Shows <paramref name="request"/> (after those waiting); its answer.</summary>
    public Task<DialogResult> ShowAsync(DialogRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(DialogResult.None);
        }

        var pending = new Pending(request);
        bool shown;
        lock (_gate)
        {
            shown = _current is null;
            if (shown)
            {
                Open(pending);
            }
            else
            {
                _waiting.Enqueue(pending);
            }
        }

        if (cancellationToken.CanBeCanceled)
        {
            pending.Registration = cancellationToken.Register(() => Remove(pending));
        }

        if (shown)
        {
            Raise(Changed);
        }

        return pending.Answer.Task;
    }

    /// <summary>Answers the dialog shown with action <paramref name="index"/> (and the field's <paramref name="text"/>); false when none shows or the index is not an action.</summary>
    public bool Answer(int index, string? text = null)
    {
        Pending? answered;
        lock (_gate)
        {
            if (_current is null || index < -1 || index >= _current.Request.Actions.Count)
            {
                return false;
            }

            answered = _current;
            Next();
        }

        answered.Registration.Dispose();
        answered.Answer.TrySetResult(new DialogResult(index, answered.Request.InputPlaceholder is null ? null : text ?? ""));
        Raise(Changed);
        return true;
    }

    /// <summary>Escape: answers the dialog's way out (<see cref="DialogRequest.CancelIndex"/>, or -1); false when none shows.</summary>
    public bool Cancel()
    {
        var current = Current;
        return current is not null && Answer(current.CancelIndex);
    }

    /// <summary>Enter: answers the focused action, else the default one (with the field's <paramref name="text"/>); false when none shows.</summary>
    public bool Confirm(string? text = null)
    {
        DialogRequest? current;
        int focused;
        lock (_gate)
        {
            current = _current?.Request;
            focused = _focused;
        }

        if (current is null)
        {
            return false;
        }

        int index = focused >= 0 ? focused : current.DefaultIndex;
        return Answer(index, text);
    }

    /// <summary>Moves the focus <paramref name="step"/> actions on (negative: back), wrapping at either end (the focus stays in the dialog); the action now focused, or -1.</summary>
    public int MoveFocus(int step)
    {
        int focused;
        lock (_gate)
        {
            if (_current is not { Request.Actions.Count: > 0 and var count })
            {
                return -1;
            }

            int from = _focused < 0 ? (step > 0 ? -1 : 0) : _focused;
            _focused = (((from + step) % count) + count) % count;
            focused = _focused;
        }

        Raise(FocusMoved);
        return focused;
    }

    /// <summary>Sets the focused action (the view's pointer or its own focus moved there); -1 for the text field.</summary>
    public void SetFocus(int index)
    {
        lock (_gate)
        {
            if (_current is not null && index >= -1 && index < _current.Request.Actions.Count)
            {
                _focused = index;
            }
        }
    }

    /// <summary>
    /// The dialog's keys while one shows (Escape, Enter, Tab and Shift+Tab, the arrows in a list; every other key is taken
    /// so nothing behind it runs): true when it took the key. With none showing, false.
    /// </summary>
    public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor)
    {
        var current = Current;
        if (current is null)
        {
            return false;
        }

        bool plain = (modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0;
        switch (key)
        {
            case EditorKey.Escape when plain:
                Cancel();
                return true;
            case EditorKey.Enter when plain:
                // In the dialog's text field Enter is the field's own (its Completed confirms with the text).
                if (current.InputPlaceholder is null || Focused >= 0)
                {
                    Confirm();
                }

                return true;
            case EditorKey.Tab when plain:
                MoveFocus((modifiers & KeyModifiers.Shift) != 0 ? -1 : 1);
                return true;
            case EditorKey.Down or EditorKey.Right when plain && (current.IsList || key == EditorKey.Right):
                MoveFocus(1);
                return true;
            case EditorKey.Up or EditorKey.Left when plain && (current.IsList || key == EditorKey.Left):
                MoveFocus(-1);
                return true;
            default:
                return true;                                     // modal: nothing behind the dialog takes a key
        }
    }

    /// <summary>
    /// The window's key handler with the dialogs first: while one shows it takes every key; otherwise a key from the
    /// window (no editor) goes to <paramref name="inner"/> only when it is a chord (Ctrl or Alt) or a function key, as the
    /// key bindings expect, and an editor's key goes to <paramref name="inner"/> as it is.
    /// </summary>
    public IEditorKeyHandler Before(IEditorKeyHandler inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return new Filter(this, inner);
    }

    private void Open(Pending pending)
    {
        _current = pending;
        _focused = pending.Request.InputPlaceholder is not null ? -1 : pending.Request.DefaultIndex;
    }

    // Under the gate: the next waiting dialog becomes the current one.
    private void Next()
    {
        _current = null;
        _focused = -1;
        if (_waiting.TryDequeue(out var next))
        {
            Open(next);
        }
    }

    private void Remove(Pending pending)
    {
        bool wasCurrent;
        lock (_gate)
        {
            wasCurrent = ReferenceEquals(_current, pending);
            if (wasCurrent)
            {
                Next();
            }
            else if (_waiting.Contains(pending))
            {
                var kept = _waiting.Where(p => !ReferenceEquals(p, pending)).ToArray();
                _waiting.Clear();
                foreach (var p in kept)
                {
                    _waiting.Enqueue(p);
                }
            }
        }

        pending.Answer.TrySetResult(DialogResult.None);
        if (wasCurrent)
        {
            Raise(Changed);
        }
    }

    private void Raise(EventHandler? handler)
    {
        if (handler is not null)
        {
            _dispatch(() => handler(this, EventArgs.Empty));
        }
    }

    private sealed class Pending(DialogRequest request)
    {
        public DialogRequest Request { get; } = request;

        public TaskCompletionSource<DialogResult> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenRegistration Registration { get; set; }
    }

    private sealed class Filter(DialogQueue dialogs, IEditorKeyHandler inner) : IEditorKeyHandler
    {
        public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor)
        {
            if (dialogs.HandleKey(key, modifiers, editor))
            {
                return true;
            }

            if (editor is null)
            {
                bool chord = (modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0;
                bool function = key is EditorKey.F3 or (>= EditorKey.F1 and <= EditorKey.F12);
                if (!chord && !function)
                {
                    return false;                                // a plain key typed into a text box is that box's
                }
            }

            return inner.HandleKey(key, modifiers, editor);
        }
    }
}
