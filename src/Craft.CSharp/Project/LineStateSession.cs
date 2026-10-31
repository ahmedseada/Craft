// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using Craft.Abstractions;

namespace Craft.CSharp.Project;

/// <summary>
/// A language's colours, one line at a time: <see cref="Tokenize"/> classifies a line (no line break) from the state the
/// line above ended in and returns the state this one ends in (inside a comment, a string, a tag, a C# block). States are
/// plain numbers, so a session keeps one per line and compares them; a tokenizer holds nothing between calls (one
/// instance serves every session) and adds its spans in order, from shared class names, with no allocation.
/// </summary>
internal abstract class LineTokenizer
{
    /// <summary>The state a document starts in.</summary>
    public virtual ulong Initial => 0;

    /// <summary>
    /// Classifies <paramref name="line"/>, whose first character is at <paramref name="lineStart"/> of the snapshot, from
    /// <paramref name="state"/>, adding its spans to <paramref name="into"/> (null: only the end state is wanted).
    /// </summary>
    public abstract ulong Tokenize(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into);

    /// <summary>Adds a span when spans are wanted.</summary>
    protected static void Add(TokenSpanBuffer? into, int start, int length, string tokenClass) => into?.Add(start, length, tokenClass);
}

/// <summary>
/// A document's formatter for a language that formats (XML, JSON): the edits that format a whole text, in order, not
/// overlapping, each replacing only the white space between two of its tokens (so the caret keeps its place and the
/// text's own characters never change). A text it cannot format throws <see cref="FormatException"/> saying where.
/// </summary>
internal interface ITextFormatter
{
    /// <summary>Adds to <paramref name="edits"/> the edits that format <paramref name="text"/> in <paramref name="style"/>.</summary>
    /// <exception cref="FormatException">The text is malformed; the message says the line, the column and what is wrong.</exception>
    void Format(ReadOnlySpan<char> text, FormatStyle style, List<TextEdit> edits);
}

/// <summary>How a formatter lays text out: one indentation step, and the line break it inserts.</summary>
/// <param name="Indent">One indentation step (spaces of the tab size, or a tab).</param>
/// <param name="NewLine">The line break.</param>
internal sealed record FormatStyle(string Indent, string NewLine)
{
    private readonly string?[] _breaks = new string?[64];

    /// <summary>A line break followed by <paramref name="depth"/> indentation steps (after <paramref name="blankLines"/> empty lines): one string per shape, made once.</summary>
    public string Break(int depth, int blankLines = 0)
    {
        int key = (depth * 2) + Math.Min(blankLines, 1);
        if (key >= _breaks.Length)
        {
            return Make(depth, blankLines);
        }

        return _breaks[key] ??= Make(depth, blankLines);
    }

    private string Make(int depth, int blankLines)
    {
        var text = new System.Text.StringBuilder();
        text.Append(NewLine);
        if (blankLines > 0)
        {
            text.Append(NewLine);
        }

        for (int i = 0; i < depth; i++)
        {
            text.Append(Indent);
        }

        return text.ToString();
    }

    /// <summary>The style of <paramref name="settings"/> for <paramref name="snapshot"/>: its tab size and spaces, the document's own line break.</summary>
    public static FormatStyle For(LanguageSettings settings, ITextSnapshot snapshot)
    {
        string indent = settings.InsertSpaces ? new string(' ', Math.Max(1, settings.TabSize)) : "\t";
        string newLine = settings.NewLine
            ?? (snapshot.LineCount > 1 ? (snapshot.GetLine(0).BreakLength == 2 ? "\r\n" : snapshot[snapshot.GetLine(0).End] == '\r' ? "\r" : "\n") : Environment.NewLine);
        return new FormatStyle(indent, newLine);
    }
}

/// <summary>
/// A session of a line-state language (<see cref="ProjectLanguage"/>): the colours of the lines asked for, from the state
/// each line above ended in, kept per line and made again only from the first line a change touched. After a change the
/// states below it are kept (shifted by the lines it added or removed) and trusted again as soon as a line below the
/// change ends in the state it ended in before, so typing in a large file classifies only the lines on screen and those
/// down to the first line that settles. Classification runs on the calling thread (the UI's), never waits, and needs no
/// parse: every snapshot is classified completely when asked. Formatting runs off the calling thread.
/// </summary>
internal sealed class LineStateSession : IFormattingLanguageSession
{
    private const int KeptLineBuffer = 4096;
    private readonly ITextBuffer _buffer;
    private readonly LineTokenizer _tokenizer;
    private readonly ITextFormatter? _formatter;
    private readonly LanguageSettings _settings;
    private readonly Lock _gate = new();
    private ITextSnapshot _snapshot;
    private ulong[] _ends = new ulong[256];
    private int _valid;
    private int _settleFrom = -1;
    private int _settleTo = -1;
    private char[] _line = new char[256];
    private bool _disposed;

    public LineStateSession(ITextBuffer buffer, LineTokenizer tokenizer, ITextFormatter? formatter, LanguageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
        _tokenizer = tokenizer;
        _formatter = formatter;
        _settings = settings;
        _snapshot = buffer.Current;
        buffer.Changed += OnChanged;
    }

    /// <summary>The lines whose end state is known (the tests' view of how far a change made it classify again).</summary>
    internal int KnownLines
    {
        get
        {
            lock (_gate)
            {
                return _valid;
            }
        }
    }

    /// <summary>The lines classified to learn their state since the session opened (the tests' measure of the work done).</summary>
    internal long LinesScanned { get; private set; }

    /// <inheritdoc />
    public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(into);
        if (snapshot.Length == 0)
        {
            return;
        }

        start = Math.Clamp(start, 0, snapshot.Length);
        int first = snapshot.GetLineIndex(start);
        int last = snapshot.GetLineIndex(Math.Clamp(start + Math.Max(0, length), 0, snapshot.Length));
        lock (_gate)
        {
            if (!ReferenceEquals(snapshot, _snapshot))
            {
                ClassifyAlone(snapshot, first, last, into);
                return;
            }

            Known(first);
            ulong state = first == 0 ? _tokenizer.Initial : _ends[first - 1];
            for (int index = first; index <= last; index++)
            {
                var line = snapshot.GetLine(index);
                state = _tokenizer.Tokenize(Read(snapshot, line, out var rented), line.Start, state, into);
                Return(rented);
                if (index == _valid)
                {
                    Learn(index, state);
                }
            }
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TextEdit>> FormatAsync(ITextSnapshot snapshot, int start, int length, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_formatter is not { } formatter)
        {
            return Task.FromResult<IReadOnlyList<TextEdit>>([]);
        }

        if (length >= 0 && (start < 0 || start > snapshot.Length - length))
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, $"The range of {length} characters at {start} runs outside the text of {snapshot.Length}.");
        }

        var style = FormatStyle.For(_settings, snapshot);
        return Task.Run<IReadOnlyList<TextEdit>>(() =>
        {
            char[] text = ArrayPool<char>.Shared.Rent(Math.Max(1, snapshot.Length));
            try
            {
                snapshot.CopyTo(0, text.AsSpan(0, snapshot.Length));
                cancellationToken.ThrowIfCancellationRequested();
                var edits = new List<TextEdit>();
                formatter.Format(text.AsSpan(0, snapshot.Length), style, edits);
                if (length >= 0)
                {
                    edits.RemoveAll(e => e.Start < start || e.End > start + length);
                }

                return edits;
            }
            finally
            {
                ArrayPool<char>.Shared.Return(text);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _buffer.Changed -= OnChanged;
            _ends = [];
            _line = [];
        }
    }

    // A change: the states above its first line stay; those from its last line down are kept, shifted, to settle on.
    private void OnChanged(object? sender, TextChangedEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _settleFrom = _settleTo = -1;
            if (!ReferenceEquals(e.Before, _snapshot) || e.Edits.Count == 0)
            {
                _valid = ReferenceEquals(e.Before, _snapshot) ? _valid : 0;
                _snapshot = e.After;
                return;
            }

            // The change's last line keeps its old end state too: when it ends as it did, the unchanged lines below it
            // start as they did, so the states below are right again.
            int first = e.Before.GetLineIndex(e.Edits[0].Start);
            int lastBefore = e.Before.GetLineIndex(e.Edits[^1].End);
            int delta = e.After.LineCount - e.Before.LineCount;
            int from = lastBefore;
            if (_valid > from)
            {
                int count = _valid - from;
                Grow(from + delta + count);
                Array.Copy(_ends, from, _ends, from + delta, count);
                _settleFrom = from + delta;
                _settleTo = from + delta + count;
            }

            _valid = Math.Min(_valid, first);
            _snapshot = e.After;
        }
    }

    // Learns the end states of the lines above `line`.
    private void Known(int line)
    {
        while (_valid < line)
        {
            int index = _valid;
            ulong state = index == 0 ? _tokenizer.Initial : _ends[index - 1];
            var text = _snapshot.GetLine(index);
            state = _tokenizer.Tokenize(Read(_snapshot, text, out var rented), text.Start, state, null);
            Return(rented);
            LinesScanned++;
            Learn(index, state);
        }
    }

    // Records the end state of line `index` (== _valid); a line below the last change that ends as it did settles the rest.
    private void Learn(int index, ulong state)
    {
        if (index >= _settleFrom && index < _settleTo)
        {
            if (_ends[index] == state)
            {
                _valid = _settleTo;
                _settleFrom = _settleTo = -1;
                return;
            }
        }
        else if (index >= _settleTo)
        {
            _settleFrom = _settleTo = -1;
        }

        Grow(index + 1);
        _ends[index] = state;
        _valid = index + 1;
    }

    private void Grow(int lines)
    {
        if (lines > _ends.Length)
        {
            Array.Resize(ref _ends, Math.Max(lines, _ends.Length * 2));
        }
    }

    // A snapshot that is not the session's current one (an older one a caller kept): classified from the top, nothing kept.
    private void ClassifyAlone(ITextSnapshot snapshot, int first, int last, TokenSpanBuffer into)
    {
        ulong state = _tokenizer.Initial;
        for (int index = 0; index <= last; index++)
        {
            var line = snapshot.GetLine(index);
            state = _tokenizer.Tokenize(Read(snapshot, line, out var rented), line.Start, state, index >= first ? into : null);
            Return(rented);
        }
    }

    // The line's characters: the session's own buffer up to a size, a pooled one for a longer line (returned by Return).
    private ReadOnlySpan<char> Read(ITextSnapshot snapshot, TextLine line, out char[]? rented)
    {
        rented = null;
        if (line.Length > _line.Length)
        {
            if (line.Length <= KeptLineBuffer)
            {
                _line = new char[Math.Min(KeptLineBuffer, Math.Max(line.Length, _line.Length * 2))];
            }
            else
            {
                rented = ArrayPool<char>.Shared.Rent(line.Length);
                snapshot.CopyTo(line.Start, rented.AsSpan(0, line.Length));
                return rented.AsSpan(0, line.Length);
            }
        }

        var span = _line.AsSpan(0, line.Length);
        snapshot.CopyTo(line.Start, span);
        return span;
    }

    private static void Return(char[]? rented)
    {
        if (rented is not null)
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }
}

/// <summary>
/// A language of a C# project's files that colours by lines (<see cref="LineTokenizer"/>) and may format
/// (<see cref="ITextFormatter"/>): registered by the C# plug-in through <see cref="EditorLanguages"/>, as any plug-in can.
/// </summary>
internal sealed class ProjectLanguage(string name, string summary, string[] extensions, LanguageRules rules, LineTokenizer tokenizer, ITextFormatter? formatter) : ILanguage
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public string Summary { get; } = summary;

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions { get; } = extensions;

    /// <inheritdoc />
    public LanguageRules Rules { get; } = rules;

    /// <summary>Whether Format document formats its files.</summary>
    public bool Formats => formatter is not null;

    /// <inheritdoc />
    public ILanguageSession Open(ITextBuffer buffer) => new LineStateSession(buffer, tokenizer, formatter, LanguageSettings.Default);

    /// <inheritdoc />
    public ILanguageSession Open(ITextBuffer buffer, LanguageOpenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new LineStateSession(buffer, tokenizer, formatter, context.Settings);
    }
}
