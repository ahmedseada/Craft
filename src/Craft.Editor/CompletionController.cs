// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// The completion list of one editor, on the UI thread. It opens after an identifier character or '.' is typed (once
/// <see cref="Core.CraftOptions.CompletionDelayMilliseconds"/> pass without another keystroke) or at once on Ctrl+Space;
/// the request runs off the UI thread and its answer is taken only while the caret is still in the word it was asked for
/// (a later request, or a caret that left the word, drops it: <see cref="ResultsDropped"/>). Typing filters the items by
/// prefix (case first) or camel humps ("WL" for WriteLine) and selects the best; Up, Down, Page Up and Page Down select;
/// Enter and Tab commit (the item's edit, mapped onto the text typed since, as one undo step); Escape closes. Typing never
/// waits for it.
/// </summary>
public sealed class CompletionController : IDisposable
{
    private readonly EditorModel _model;
    private readonly List<CompletionItem> _filtered = [];
    private EditorModel.DebounceTimer? _timer;
    private CancellationTokenSource? _request;
    private int _requestId;
    private CompletionList? _list;
    private int _selected;
    private int _firstVisible;

    internal CompletionController(EditorModel model) => _model = model;

    /// <summary>Whether the list shows.</summary>
    public bool IsOpen => _list is not null && _filtered.Count > 0;

    /// <summary>The list as the language gave it (null while closed).</summary>
    public CompletionList? List => _list;

    /// <summary>The items that match what is typed, in the list's order.</summary>
    public IReadOnlyList<CompletionItem> Items => _filtered;

    /// <summary>The selected item's index in <see cref="Items"/>.</summary>
    public int SelectedIndex => _selected;

    /// <summary>The selected item, or null while closed.</summary>
    public CompletionItem? Selected => IsOpen ? _filtered[_selected] : null;

    /// <summary>The first row shown (the list scrolls to keep the selection in its rows).</summary>
    public int FirstVisible => _firstVisible;

    /// <summary>The text typed in the word so far (from the list's span start to the caret).</summary>
    public string Filter { get; private set; } = "";

    /// <summary>The requests made since the editor opened.</summary>
    public long Requests { get; private set; }

    /// <summary>The answers dropped: a newer request was made, or the caret left the word before it arrived.</summary>
    public long ResultsDropped { get; private set; }

    /// <summary>Raised when the list opened, closed, filtered or moved its selection.</summary>
    public event EventHandler? Changed;

    /// <summary>Ctrl+Space: asks for the list at the caret now.</summary>
    public void Invoke()
    {
        _timer?.Cancel();
        Request(CompletionTrigger.Invoke);
    }

    /// <summary>Closes the list (and forgets any request in flight).</summary>
    public void Close()
    {
        _timer?.Cancel();
        _request?.Cancel();
        _request = null;
        _requestId++;
        if (_list is null)
        {
            return;
        }

        _list = null;
        _filtered.Clear();
        Filter = "";
        _model.Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects the item at <paramref name="index"/> of <see cref="Items"/> (clamped), scrolling the rows to show it.</summary>
    public void SelectIndex(int index)
    {
        if (!IsOpen)
        {
            return;
        }

        _selected = Math.Clamp(index, 0, _filtered.Count - 1);
        int rows = Math.Max(1, _model.Options.CompletionRows);
        if (_selected < _firstVisible)
        {
            _firstVisible = _selected;
        }
        else if (_selected >= _firstVisible + rows)
        {
            _firstVisible = _selected - rows + 1;
        }

        _firstVisible = Math.Clamp(_firstVisible, 0, Math.Max(0, _filtered.Count - rows));
        _model.Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Commits the selected item: its edit, mapped onto what was typed since the list came, as one undo step. An item whose
    /// exact edit the language resolves on commit (<see cref="CompletionItem.NeedsResolve"/>) is applied when the answer
    /// comes, if the text is still the one committed in (else it is dropped). False when closed.
    /// </summary>
    public bool Commit()
    {
        if (Selected is not { } item || _list is not { } list)
        {
            return false;
        }

        Close();
        if (item.NeedsResolve && _model.SemanticSession is { } session)
        {
            int version = _model.Snapshot.Version;
            _model.Track(session.ResolveCompletionAsync(list, item, CancellationToken.None), resolved =>
            {
                if (_model.Snapshot.Version != version)
                {
                    ResultsDropped++;
                    _model.LateResultsDropped++;
                    return;
                }

                Apply(list, resolved);
            });
            return true;
        }

        Apply(list, item);
        return true;
    }

    /// <summary>Applies <paramref name="item"/>'s edit (in <paramref name="list"/>'s positions) to the current text: its end follows what was typed since.</summary>
    private void Apply(CompletionList list, CompletionItem item)
    {
        var snapshot = _model.Snapshot;
        int delta = snapshot.Length - list.Snapshot.Length;
        var edit = item.Edit;
        int end = edit.End >= list.Position ? edit.End + delta : edit.End;
        end = Math.Clamp(end, edit.Start, snapshot.Length);
        int caret = item.CaretAfter >= 0 && item.CaretAfter <= edit.Start + edit.Text.Length ? item.CaretAfter : edit.Start + edit.Text.Length;
        _model.ApplyCompletion(new TextEdit(edit.Start, end - edit.Start, edit.Text), caret);
    }

    /// <summary>Disposes the debounce timer.</summary>
    public void Dispose()
    {
        _request?.Cancel();
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>The keys of an open list; true when handled.</summary>
    internal bool HandleKey(EditorKey key, KeyModifiers modifiers)
    {
        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0)
        {
            return false;
        }

        int rows = Math.Max(1, _model.Options.CompletionRows);
        switch (key)
        {
            case EditorKey.Up:
                SelectIndex(_selected - 1);
                return true;
            case EditorKey.Down:
                SelectIndex(_selected + 1);
                return true;
            case EditorKey.PageUp:
                SelectIndex(_selected - rows);
                return true;
            case EditorKey.PageDown:
                SelectIndex(_selected + rows);
                return true;
            case EditorKey.Enter or EditorKey.Tab when (modifiers & KeyModifiers.Shift) == 0:
                return Commit();
            case EditorKey.Escape:
                Close();
                return true;
            default:
                return false;
        }
    }

    /// <summary>After typed text: an identifier character filters an open list or (after the delay) asks for one; '.' asks for one; anything else closes it.</summary>
    internal void OnTyped(string text)
    {
        if (_model.SemanticSession is null)
        {
            return;
        }

        char last = text[^1];
        bool identifier = IsIdentifierPart(last);
        if (IsOpen || _list is not null)
        {
            if (identifier && Refilter())
            {
                return;
            }

            Close();
        }

        if (identifier && _request is not null && last != '.')
        {
            // A request is in flight for this word: its answer is filtered by what is typed meanwhile.
            return;
        }

        if ((identifier && text.Length == 1) || last == '.')
        {
            ScheduleRequest(CompletionTrigger.Typed(last));
        }
        else
        {
            _timer?.Cancel();
        }
    }

    /// <summary>After Backspace or Delete: an open list filters again (closing when the caret left its word).</summary>
    internal void OnDeleted()
    {
        if (_list is not null && !Refilter())
        {
            Close();
        }
    }

    /// <summary>After the caret moved by a key or the pointer: the list closes when the caret left its word.</summary>
    internal void OnCaretMoved()
    {
        _timer?.Cancel();
        if (_list is not null && !Refilter())
        {
            Close();
        }
    }

    /// <summary>A click on row <paramref name="row"/> of the shown rows: selects it (a second click commits).</summary>
    internal void Click(int row, int clickCount)
    {
        SelectIndex(_firstVisible + row);
        if (clickCount >= 2)
        {
            Commit();
        }
    }

    private void ScheduleRequest(CompletionTrigger trigger)
    {
        _timer ??= new EditorModel.DebounceTimer(_model);
        _timer.Schedule(_model.Options.CompletionDelayMilliseconds, () => Request(trigger));
    }

    private void Request(CompletionTrigger trigger)
    {
        if (_model.SemanticSession is not { } session)
        {
            return;
        }

        _request?.Cancel();
        var cancel = new CancellationTokenSource();
        _request = cancel;
        int id = ++_requestId;
        var snapshot = _model.Snapshot;
        int caret = _model.Selection.Caret;
        Requests++;
        _model.Track(session.GetCompletionsAsync(snapshot, caret, trigger, cancel.Token), list => Apply(id, list));
    }

    /// <summary>On the UI thread: takes the answer of request <paramref name="id"/> if it is the newest and the caret is still in its word.</summary>
    internal void Apply(int id, CompletionList? list)
    {
        if (id != _requestId)
        {
            ResultsDropped++;
            return;
        }

        _request = null;
        if (list is null || list.Items.Count == 0)
        {
            Close();
            return;
        }

        var previous = _list;
        _list = list;
        if (!Refilter())
        {
            _list = previous;
            ResultsDropped++;
            _model.LateResultsDropped++;
            Close();
        }
    }

    /// <summary>Filters the list by the word typed so far; false when the caret is no longer in the list's word or nothing matches.</summary>
    private bool Refilter()
    {
        if (_list is not { } list)
        {
            return false;
        }

        var snapshot = _model.Snapshot;
        var selection = _model.Selection;
        int caret = selection.Caret;
        int start = list.SpanStart;
        if (!selection.IsEmpty || caret < start || caret - start > 512 || start > snapshot.Length)
        {
            return false;
        }

        // The text from the span's start to the caret: identifier characters only, and unchanged where the list saw it.
        int seen = Math.Min(caret, list.Position) - start;
        if (start + seen > list.Snapshot.Length)
        {
            return false;
        }

        Span<char> typed = caret - start <= 256 ? stackalloc char[caret - start] : new char[caret - start];
        snapshot.CopyTo(start, typed);
        foreach (char c in typed)
        {
            if (!IsIdentifierPart(c))
            {
                return false;
            }
        }

        for (int i = 0; i < seen; i++)
        {
            if (list.Snapshot[start + i] != typed[i])
            {
                return false;
            }
        }

        Filter = typed.ToString();
        string filter = Filter;
        _filtered.Clear();
        int best = -1;
        int bestScore = 0;
        foreach (var item in list.Items)
        {
            int score = Score(item.FilterText, filter);
            if (score == 0)
            {
                continue;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = _filtered.Count;
            }

            _filtered.Add(item);
        }

        if (_filtered.Count == 0)
        {
            return false;
        }

        _selected = Math.Max(0, best);
        _firstVisible = 0;
        SelectIndex(_selected);
        return true;
    }

    /// <summary>How well <paramref name="text"/> matches <paramref name="typed"/>: 4 the same prefix (case too), 3 ignoring case, 2 camel humps, 1 for an empty filter, 0 none.</summary>
    public static int Score(string text, string typed)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(typed);
        if (typed.Length == 0)
        {
            return 1;
        }

        if (text.StartsWith(typed, StringComparison.Ordinal))
        {
            return 4;
        }

        if (text.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        // Leading underscores (a field's "_") are not part of the first hump.
        int first = 0;
        while (first < text.Length - 1 && text[first] == '_' && typed[0] != '_')
        {
            first++;
        }

        return Humps(text, first, typed, 0) ? 2 : 0;
    }

    /// <summary>Whether <paramref name="typed"/> (from <paramref name="t"/>) is a run of prefixes of <paramref name="text"/>'s humps (from <paramref name="at"/>, a hump start).</summary>
    private static bool Humps(string text, int at, string typed, int t)
    {
        if (t == typed.Length)
        {
            return true;
        }

        if (at >= text.Length || char.ToUpperInvariant(text[at]) != char.ToUpperInvariant(typed[t]))
        {
            return false;
        }

        // Take as many characters of this hump as match, then try the next humps, the longest first.
        int length = 1;
        while (at + length < text.Length && t + length < typed.Length && !IsHumpStart(text, at + length)
            && char.ToUpperInvariant(text[at + length]) == char.ToUpperInvariant(typed[t + length]))
        {
            length++;
        }

        for (int take = length; take >= 1; take--)
        {
            if (t + take == typed.Length)
            {
                return true;
            }

            for (int next = at + take; next < text.Length; next++)
            {
                if (IsHumpStart(text, next) && Humps(text, next, typed, t + take))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsHumpStart(string text, int at) =>
        at == 0 || char.IsUpper(text[at]) && !char.IsUpper(text[at - 1]) || (text[at - 1] == '_' && text[at] != '_') || (char.IsDigit(text[at]) && !char.IsDigit(text[at - 1]));

    /// <summary>Whether <paramref name="c"/> may be part of an identifier (letters, digits, '_', and '@' at its start).</summary>
    public static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@';
}
