// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>
/// The editor's inline completion (ghost text at the caret, plan 1 "Inline completion"): after a pause in typing (the
/// provider's <see cref="IInlineCompletionProvider.DelayMilliseconds"/>) it asks the provider of
/// <see cref="Core.CraftOptions.InlineCompletionProvider"/> (or the first registered that accepts the document) on the
/// thread pool, never on the UI thread, and shows the pieces as they stream. Any other key, typed text, a click or a change
/// cancels the request at once (a provider stops within a token) and removes the ghost; Tab inserts it all as one
/// <see cref="EditKind.Command"/> undo step, Ctrl+Right a word, Escape dismisses. Text typed that the ghost starts with
/// is typed through (the rest stays, the request stops). Used on the UI thread.
/// </summary>
public sealed class InlineCompletionController : IDisposable
{
    private readonly EditorModel _model;
    private EditorModel.DebounceTimer? _timer;
    private CancellationTokenSource? _request;
    private int _generation;
    private string _text = "";
    private long _requestStarted;
    private long _firstPieceAt;
    private bool _accepting;

    internal InlineCompletionController(EditorModel model) => _model = model;

    /// <summary>The ghost text shown at <see cref="Position"/>, or empty when none is.</summary>
    public string Text => _text;

    /// <summary>Where the ghost text goes (the caret when it was asked for).</summary>
    public int Position { get; private set; } = -1;

    /// <summary>Whether ghost text shows (and the completion list does not hide it).</summary>
    public bool IsShown => _text.Length > 0 && !_model.Completion.IsOpen;

    /// <summary>Whether a request streams now.</summary>
    public bool IsRequesting => _request is not null;

    /// <summary>The provider that answered the last request, or null.</summary>
    public string? ProviderName { get; private set; }

    /// <summary>The milliseconds from the request to its first piece, or null before it came.</summary>
    public double? FirstPieceMilliseconds { get; private set; }

    /// <summary>The tokens the shown completion took.</summary>
    public int Tokens { get; private set; }

    /// <summary>The tokens per second after the first piece (0 before a second piece).</summary>
    public double TokensPerSecond { get; private set; }

    /// <summary>The requests made since the editor opened.</summary>
    public long Requests { get; private set; }

    /// <summary>The requests cancelled before they ended (a keystroke, a click, a change).</summary>
    public long Cancelled { get; private set; }

    /// <summary>The completions accepted (whole or a word at a time).</summary>
    public long Accepted { get; private set; }

    /// <summary>Pieces that arrived for a request already cancelled (dropped, never shown).</summary>
    public long LatePieces { get; private set; }

    /// <summary>The error the last request threw (the editor went on), or null.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>Raised on the UI thread when the ghost text, its statistics or the request's state changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The hint bar's text after the keys (the design's "41 tok/s · first token 86 ms"), or "" before a piece came.</summary>
    public string StatisticsText => FirstPieceMilliseconds is { } first
        ? TokensPerSecond > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{TokensPerSecond:F0} tok/s · first token {first:F0} ms")
            : string.Create(CultureInfo.InvariantCulture, $"first token {first:F0} ms")
        : "";

    /// <summary>Asks now (no pause) at the caret; false when no provider accepts the document.</summary>
    public bool Invoke()
    {
        Cancel();
        return Start();
    }

    /// <summary>Removes the ghost text and stops its request (Escape).</summary>
    public void Dismiss() => Cancel();

    /// <summary>Inserts the whole ghost text at its position as one undo step (Tab); false when none shows.</summary>
    public bool AcceptAll()
    {
        if (!IsShown || !AtCaret())
        {
            return false;
        }

        string text = _text;
        int at = Position;
        Cancel();
        Insert(at, text);
        Accepted++;
        return true;
    }

    /// <summary>
    /// Inserts the ghost text's next word (Ctrl+Right): white space and then a run of letters, digits and '_', or one other
    /// character; a line break with the next line's indentation. The rest stays as ghost text and keeps streaming.
    /// </summary>
    public bool AcceptWord()
    {
        if (!IsShown || !AtCaret())
        {
            return false;
        }

        int length = NextWordLength(_text);
        string word = _text[..length];
        _text = _text[length..];
        int at = Position;
        Position = at + length;
        Insert(at, word);
        Accepted++;
        if (_text.Length == 0)
        {
            Cancel();
        }

        Raise();
        return true;
    }

    /// <summary>The length of the next word of <paramref name="text"/> as <see cref="AcceptWord"/> takes it (at least 1 for a non-empty text).</summary>
    public static int NextWordLength(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return 0;
        }

        int i = 0;
        if (text[0] is '\r' or '\n')
        {
            i = text[0] == '\r' && text.Length > 1 && text[1] == '\n' ? 2 : 1;
            while (i < text.Length && text[i] is ' ' or '\t')
            {
                i++;
            }

            return i;
        }

        while (i < text.Length && text[i] is ' ' or '\t')
        {
            i++;
        }

        if (i == text.Length)
        {
            return i;
        }

        if (IsWord(text[i]))
        {
            while (i < text.Length && IsWord(text[i]))
            {
                i++;
            }

            return i;
        }

        return i + (char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1);

        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CancelRequest();
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Keys while ghost text shows: Tab, Ctrl+Right and Escape are its own; any other key cancels it. True when handled.</summary>
    internal bool HandleKey(EditorKey key, KeyModifiers modifiers)
    {
        if (IsShown)
        {
            switch (key)
            {
                case EditorKey.Tab when modifiers == KeyModifiers.None:
                    return AcceptAll();
                case EditorKey.Right when modifiers == KeyModifiers.Control:
                    return AcceptWord();
                case EditorKey.Escape when modifiers == KeyModifiers.None:
                    Cancel();
                    return true;
            }
        }

        Cancel();
        return false;
    }

    /// <summary>
    /// Before typed text: text the ghost starts with is typed through (true: the rest of the ghost stays and the request
    /// stops); any other text cancels the ghost (false).
    /// </summary>
    internal bool TypeThrough(string typed)
    {
        if (IsShown && AtCaret() && _model.Selection.IsEmpty && _text.Length > typed.Length && _text.StartsWith(typed, StringComparison.Ordinal))
        {
            CancelRequest();
            _text = _text[typed.Length..];
            Position += typed.Length;
            _accepting = true;
            return true;
        }

        Cancel();
        return false;
    }

    /// <summary>After typed text or a deletion at the caret: asks again after the pause (unless the ghost was typed through).</summary>
    internal void OnEdited()
    {
        if (_accepting)
        {
            _accepting = false;
            Raise();
            return;
        }

        Schedule();
    }

    /// <summary>The text changed otherwise (an undo, an accepted diff, a reload) or the pointer moved the caret: the ghost goes.</summary>
    internal void OnChanged()
    {
        if (!_accepting)
        {
            Cancel();
        }
    }

    private bool AtCaret() => _model.Selection.IsEmpty && _model.Selection.Caret == Position;

    private void Schedule()
    {
        CancelRequest();
        var request = Request();
        var provider = request is null ? null : Provider(request);
        if (provider is null)
        {
            return;
        }

        _timer ??= new EditorModel.DebounceTimer(_model);
        _timer.Schedule(provider.DelayMilliseconds, () => Start());
    }

    private InlineCompletionRequest? Request()
    {
        var selection = _model.Selection;
        if (!selection.IsEmpty || string.Equals(_model.Options.InlineCompletionProvider, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new InlineCompletionRequest(_model.Snapshot, selection.Caret, _model.Document.FilePath, _model.Language?.Name ?? _model.LanguageName, _model.WorkspaceRoot);
    }

    private IInlineCompletionProvider? Provider(InlineCompletionRequest request)
    {
        try
        {
            return InlineCompletionProviders.For(request, _model.Options.InlineCompletionProvider);
        }
        catch (NotRegisteredException error)
        {
            LastError = error;
            return null;
        }
    }

    // On the UI thread: the request's text is the snapshot now; the provider runs on the thread pool, its pieces come back here.
    private bool Start()
    {
        CancelRequest();
        if (_model.Completion.IsOpen || Request() is not { } request || Provider(request) is not { } provider)
        {
            return false;
        }

        int generation = ++_generation;
        var source = new CancellationTokenSource();
        _request = source;
        _text = "";
        Position = request.Position;
        ProviderName = provider.Name;
        FirstPieceMilliseconds = null;
        Tokens = 0;
        TokensPerSecond = 0;
        LastError = null;
        Requests++;
        var time = _model.Time;
        _requestStarted = time.GetTimestamp();
        var token = source.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var piece in provider.CompleteAsync(request, token).ConfigureAwait(false))
                {
                    if (token.IsCancellationRequested)
                    {
                        _model.Post(() => LatePieces++);
                        break;
                    }

                    _model.Post(() => OnPiece(generation, piece));
                }

                _model.Post(() => OnEnded(generation, null));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
#pragma warning disable CA1031 // A provider's failure is kept for the status bar; the editor goes on.
            catch (Exception error)
#pragma warning restore CA1031
            {
                _model.Post(() => OnEnded(generation, error));
            }
        }, CancellationToken.None);
        Raise();
        return true;
    }

    private void OnPiece(int generation, InlineCompletionPiece piece)
    {
        if (generation != _generation || _request is null)
        {
            LatePieces++;
            return;
        }

        var time = _model.Time;
        long now = time.GetTimestamp();
        if (FirstPieceMilliseconds is null)
        {
            FirstPieceMilliseconds = time.GetElapsedTime(_requestStarted, now).TotalMilliseconds;
            _firstPieceAt = now;
        }
        else
        {
            double seconds = time.GetElapsedTime(_firstPieceAt, now).TotalSeconds;
            TokensPerSecond = seconds > 0 ? Tokens / seconds : 0;
        }

        Tokens += piece.Tokens;
        if (piece.Text.Length > 0)
        {
            _text += piece.Text;
        }

        Raise();
    }

    private void OnEnded(int generation, Exception? error)
    {
        if (generation != _generation)
        {
            return;
        }

        _request = null;
        if (error is not null)
        {
            LastError = error;
            _text = "";
        }

        Raise();
    }

    private void Insert(int at, string text)
    {
        _accepting = true;
        try
        {
            _model.ApplyAcceptedText(at, text);
        }
        finally
        {
            _accepting = false;
        }
    }

    private void Cancel()
    {
        _timer?.Cancel();
        bool had = _text.Length > 0 || _request is not null;
        CancelRequest();
        _text = "";
        Position = -1;
        if (had)
        {
            Raise();
        }
    }

    private void CancelRequest()
    {
        if (_request is { } request)
        {
            _request = null;
            _generation++;
            Cancelled++;
            // Not disposed here: the provider may still hold its token (it stops within a token); the stream's end disposes nothing it shares.
            request.Cancel();
        }
    }

    private void Raise()
    {
        _model.Invalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
