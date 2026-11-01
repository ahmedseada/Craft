// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Globalization;

namespace Craft.Abstractions;

// The line-state kit (phase W, contract 1.1, additions): what a language that colours a line at a time from the state the
// line above ended in needs, public so a third party's language does what Craft's own plug-ins do (the C# plug-in's
// project languages, the JSON plug-in, the web languages of phase W). A tokenizer classifies one line; a session keeps
// one state per line and classifies only the lines asked for; a formatter changes only the white space between tokens.

/// <summary>
/// A language's colours, one line at a time: <see cref="Tokenize"/> classifies a line (no line break) from the state the
/// line above ended in and returns the state this one ends in (inside a comment, a string, a tag, a template). States are
/// plain numbers, so a session keeps one per line and compares them; a state that needs more than 64 bits (a stack of
/// template, JSX or embedded-language frames) is interned in a <see cref="LineStates"/> table, which gives it a number.
/// A tokenizer holds nothing between calls (one instance serves every session, on any thread) and adds its spans in
/// order, from shared class names (<see cref="TokenClasses"/>), with no allocation.
/// </summary>
public abstract class LineTokenizer
{
    /// <summary>The state a document starts in.</summary>
    public virtual ulong Initial => 0;

    /// <summary>
    /// Classifies <paramref name="line"/>, whose first character is at <paramref name="lineStart"/> of the snapshot, from
    /// <paramref name="state"/>, adding its spans to <paramref name="into"/> (null: only the end state is wanted).
    /// </summary>
    /// <returns>The state the line ends in (the next line starts in it).</returns>
    public abstract ulong Tokenize(ReadOnlySpan<char> line, int lineStart, ulong state, TokenSpanBuffer? into);

    /// <summary>
    /// The editing rules at <paramref name="column"/> of <paramref name="line"/>, which starts in <paramref name="state"/>:
    /// those of an embedded language there (an HTML file's <c>&lt;script&gt;</c> comments with <c>//</c>, its
    /// <c>&lt;style&gt;</c> with <c>/* */</c>), or null for the language's own (the default).
    /// </summary>
    public virtual LanguageRules? RulesAt(ReadOnlySpan<char> line, int column, ulong state) => null;

    /// <summary>
    /// Whether its tags are linked (X part 9's linked tag rename, contract 1.1): a markup '&lt;' and '&lt;/' coloured as
    /// <see cref="TokenClasses.Punctuation"/> with the element's name right after, and "/>" as punctuation too, so the
    /// session can find a start tag's end tag (<see cref="LineStateSession.GetLinkedEditingRanges"/>). False by default.
    /// </summary>
    public virtual bool LinksTags => false;

    /// <summary>Whether an element of <paramref name="name"/> has no end tag (HTML's br, img, input). None by default.</summary>
    public virtual bool IsVoidElement(ReadOnlySpan<char> name) => false;

    /// <summary>Adds a span when spans are wanted.</summary>
    protected static void Add(TokenSpanBuffer? into, int start, int length, string tokenClass) => into?.Add(start, length, tokenClass);
}

/// <summary>
/// The states of a <see cref="LineTokenizer"/> that do not fit in 64 bits, interned: a stack of frames (a template literal
/// inside a <c>${</c> inside a template, a JSX element inside an expression, a script inside HTML) gets one number, the
/// same number for the same frames, so a session compares states as numbers. An interned state has
/// <see cref="InternedBit"/> set, which a tokenizer's own plain states leave clear. The table is bounded (rule 77): it
/// holds at most <see cref="Capacity"/> stacks of at most <see cref="MaxDepth"/> frames, and a stack past either gets
/// <see cref="Fallback"/> (a reset state the tokenizer starts over from, counted in <see cref="Overflows"/>) instead of
/// growing it. Thread-safe; looking up a stack already interned allocates nothing.
/// </summary>
public sealed class LineStates
{
    /// <summary>The bit an interned state has set (the tokenizer's plain states leave it clear).</summary>
    public const ulong InternedBit = 1UL << 63;

    /// <summary>The stacks a table holds by default.</summary>
    public const int DefaultCapacity = 4096;

    /// <summary>The frames a stack may have by default.</summary>
    public const int DefaultMaxDepth = 64;

    private readonly Lock _gate = new();
    private ulong[] _frames = new ulong[64];
    private int _used;
    private int[] _starts = new int[16];
    private int[] _lengths = new int[16];
    private int[] _buckets = new int[32];    // id + 1 per slot, 0 empty; open addressing, a power of two
    private int _count;
    private long _overflows;

    /// <summary>A table of at most <paramref name="capacity"/> stacks of at most <paramref name="maxDepth"/> frames, past which <see cref="Intern"/> gives <paramref name="fallback"/>.</summary>
    /// <param name="capacity">The stacks it holds (at least 1).</param>
    /// <param name="maxDepth">The frames a stack may have (at least 1).</param>
    /// <param name="fallback">The state a stack that does not fit gets: a plain state of the tokenizer's (the document's start, usually) it resumes from.</param>
    public LineStates(int capacity = DefaultCapacity, int maxDepth = DefaultMaxDepth, ulong fallback = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDepth, 1);
        Capacity = capacity;
        MaxDepth = maxDepth;
        Fallback = fallback;
    }

    /// <summary>The stacks it holds at most.</summary>
    public int Capacity { get; }

    /// <summary>The frames a stack may have.</summary>
    public int MaxDepth { get; }

    /// <summary>The state a stack that does not fit gets.</summary>
    public ulong Fallback { get; }

    /// <summary>The stacks interned so far.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    /// <summary>The stacks that did not fit (the table full, or deeper than <see cref="MaxDepth"/>) and got <see cref="Fallback"/>.</summary>
    public long Overflows => Interlocked.Read(ref _overflows);

    /// <summary>Whether <paramref name="state"/> is an interned one (its <see cref="InternedBit"/> set).</summary>
    public static bool IsInterned(ulong state) => (state & InternedBit) != 0;

    /// <summary>The state of <paramref name="frames"/>: the same number for the same frames; <see cref="Fallback"/> when they do not fit.</summary>
    public ulong Intern(ReadOnlySpan<ulong> frames) => TryIntern(frames, out ulong state) ? state : Fallback;

    /// <summary>The state of <paramref name="frames"/>; false (and <see cref="Fallback"/>) when they do not fit.</summary>
    public bool TryIntern(ReadOnlySpan<ulong> frames, out ulong state)
    {
        if (frames.Length > MaxDepth)
        {
            Interlocked.Increment(ref _overflows);
            state = Fallback;
            return false;
        }

        int hash = Hash(frames);
        lock (_gate)
        {
            int mask = _buckets.Length - 1;
            int slot = hash & mask;
            while (_buckets[slot] != 0)
            {
                int id = _buckets[slot] - 1;
                if (_frames.AsSpan(_starts[id], _lengths[id]).SequenceEqual(frames))
                {
                    state = InternedBit | (uint)id;
                    return true;
                }

                slot = (slot + 1) & mask;
            }

            if (_count >= Capacity)
            {
                _overflows++;
                state = Fallback;
                return false;
            }

            int added = _count++;
            if (added >= _starts.Length)
            {
                Array.Resize(ref _starts, _starts.Length * 2);
                Array.Resize(ref _lengths, _lengths.Length * 2);
            }

            if (_used + frames.Length > _frames.Length)
            {
                Array.Resize(ref _frames, Math.Max(_used + frames.Length, _frames.Length * 2));
            }

            frames.CopyTo(_frames.AsSpan(_used));
            _starts[added] = _used;
            _lengths[added] = frames.Length;
            _used += frames.Length;
            _buckets[slot] = added + 1;
            if (_count * 2 > _buckets.Length)
            {
                Rehash();
            }

            state = InternedBit | (uint)added;
            return true;
        }
    }

    /// <summary>The frames of the interned <paramref name="state"/>, as they were interned (valid for as long as the caller keeps them: a table only adds).</summary>
    /// <exception cref="ArgumentException"><paramref name="state"/> is not one this table gave.</exception>
    public ReadOnlySpan<ulong> Frames(ulong state)
    {
        lock (_gate)
        {
            ulong id = state & ~InternedBit;
            if (!IsInterned(state) || id >= (ulong)_count)
            {
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"The state {state:X} is not one this table interned ({_count} held)."), nameof(state));
            }

            return _frames.AsSpan(_starts[(int)id], _lengths[(int)id]);
        }
    }

    // FNV-1a over the frames' 64-bit values, folded to an int (no allocation, no generic T).
    private static int Hash(ReadOnlySpan<ulong> frames)
    {
        ulong hash = 14695981039346656037UL;
        foreach (ulong frame in frames)
        {
            hash = (hash ^ frame) * 1099511628211UL;
            hash ^= hash >> 29;
        }

        hash ^= (ulong)frames.Length * 0x9E3779B97F4A7C15UL;
        return (int)(hash ^ (hash >> 32)) & int.MaxValue;
    }

    private void Rehash()
    {
        var buckets = new int[_buckets.Length * 2];
        int mask = buckets.Length - 1;
        for (int id = 0; id < _count; id++)
        {
            int slot = Hash(_frames.AsSpan(_starts[id], _lengths[id])) & mask;
            while (buckets[slot] != 0)
            {
                slot = (slot + 1) & mask;
            }

            buckets[slot] = id + 1;
        }

        _buckets = buckets;
    }
}

/// <summary>
/// A document's formatter for a language that formats (XML, JSON, CSS): the edits that format a whole text, in order, not
/// overlapping, each replacing only the white space between two of its tokens (so the caret keeps its place and the
/// text's own characters never change). A text it cannot format throws <see cref="FormatException"/> saying where
/// (<see cref="Malformed"/> makes that message).
/// </summary>
public interface ITextFormatter
{
    /// <summary>Adds to <paramref name="edits"/> the edits that format <paramref name="text"/> in <paramref name="style"/>.</summary>
    /// <exception cref="FormatException">The text is malformed; the message says the line, the column and what is wrong.</exception>
    void Format(ReadOnlySpan<char> text, FormatStyle style, List<TextEdit> edits);

    /// <summary>The error of a malformed <paramref name="text"/> at <paramref name="position"/>: "line 3, column 9: <paramref name="what"/>" (both from 1).</summary>
    static FormatException Malformed(ReadOnlySpan<char> text, int position, string what)
    {
        position = Math.Clamp(position, 0, text.Length);
        int line = 1;
        int lineStart = 0;
        for (int i = 0; i < position; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return new FormatException(string.Create(CultureInfo.InvariantCulture, $"line {line}, column {position - lineStart + 1}: {what}"));
    }
}

/// <summary>How a formatter lays text out: one indentation step, and the line break it inserts. Two styles are equal when both are.</summary>
/// <param name="Indent">One indentation step (spaces of the tab size, or a tab).</param>
/// <param name="NewLine">The line break.</param>
public sealed record FormatStyle(string Indent, string NewLine)
{
    private readonly string?[] _breaks = new string?[64];

    /// <summary>A line break followed by <paramref name="depth"/> indentation steps (after <paramref name="blankLines"/> empty lines, at most one): one string per shape, made once.</summary>
    public string Break(int depth, int blankLines = 0)
    {
        int key = (depth * 2) + Math.Min(blankLines, 1);
        if (key >= _breaks.Length)
        {
            return Make(depth, blankLines);
        }

        return _breaks[key] ??= Make(depth, blankLines);
    }

    /// <summary>The style of <paramref name="settings"/> for <paramref name="snapshot"/>: its tab size and spaces, the document's own line break.</summary>
    public static FormatStyle For(LanguageSettings settings, ITextSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(snapshot);
        string indent = settings.InsertSpaces ? new string(' ', Math.Max(1, settings.TabSize)) : "\t";
        string newLine = settings.NewLine
            ?? (snapshot.LineCount > 1 ? (snapshot.GetLine(0).BreakLength == 2 ? "\r\n" : snapshot[snapshot.GetLine(0).End] == '\r' ? "\r" : "\n") : Environment.NewLine);
        return new FormatStyle(indent, newLine);
    }

    /// <inheritdoc />
    public bool Equals(FormatStyle? other) => other is not null && string.Equals(Indent, other.Indent, StringComparison.Ordinal) && string.Equals(NewLine, other.NewLine, StringComparison.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Indent, NewLine);

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
}

/// <summary>
/// A session of a line-state language (<see cref="LineStateLanguage"/>): the colours of the lines asked for, from the state
/// each line above ended in, kept per line and made again only from the first line a change touched. After a change the
/// states below it are kept (shifted by the lines it added or removed) and trusted again as soon as a line below the
/// change ends in the state it ended in before, so typing in a large file classifies only the lines on screen and those
/// down to the first line that settles. Classification runs on the calling thread (the UI's), never waits, and needs no
/// parse: every snapshot is classified completely when asked. Formatting runs off the calling thread. Its dialect (the
/// tokenizer: a language version a project's files name, learned after the session opened) can change
/// (<see cref="ChangeDialect"/>): the states are dropped and <see cref="Reclassified"/> is raised.
/// </summary>
public sealed class LineStateSession : IFormattingLanguageSession
{
    private const int KeptLineBuffer = 4096;
    private readonly ITextBuffer _buffer;
    private readonly ITextFormatter? _formatter;
    private readonly LanguageSettings _settings;
    private readonly Lock _gate = new();
    private LineTokenizer _tokenizer;
    private ITextSnapshot _snapshot;
    private ulong[] _ends = new ulong[256];
    private int _valid;
    private int _settleFrom = -1;
    private int _settleTo = -1;
    private char[] _line = new char[256];
    private long _scanned;
    private bool _disposed;

    /// <summary>A session over <paramref name="buffer"/> (it follows the buffer's changes until disposed).</summary>
    /// <param name="buffer">The document's buffer.</param>
    /// <param name="tokenizer">The language's (or dialect's) tokenizer.</param>
    /// <param name="formatter">Its formatter, or null when the language does not format.</param>
    /// <param name="settings">The editor's settings for the language (the formatter's indentation and line break).</param>
    public LineStateSession(ITextBuffer buffer, LineTokenizer tokenizer, ITextFormatter? formatter, LanguageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(settings);
        _buffer = buffer;
        _tokenizer = tokenizer;
        _formatter = formatter;
        _settings = settings;
        _snapshot = buffer.Current;
        buffer.Changed += OnChanged;
    }

    /// <summary>Raised (on the thread that called <see cref="ChangeDialect"/>) when the dialect changed: every line's colours may have.</summary>
    public event EventHandler? Reclassified;

    /// <summary>The tokenizer the session classifies with (its dialect).</summary>
    public LineTokenizer Tokenizer
    {
        get
        {
            lock (_gate)
            {
                return _tokenizer;
            }
        }
    }

    /// <summary>The lines whose end state is known (how far a change made it classify again).</summary>
    public int KnownLines
    {
        get
        {
            lock (_gate)
            {
                return _valid;
            }
        }
    }

    /// <summary>The lines classified to learn their state since the session opened (the measure of the work done).</summary>
    public long LinesScanned => Interlocked.Read(ref _scanned);

    /// <summary>
    /// Classifies with <paramref name="tokenizer"/> from now on (a dialect learned later: the project's language version):
    /// the states kept are dropped (every line is read again when next asked for) and <see cref="Reclassified"/> is raised.
    /// Any thread. False when it is the tokenizer in use already or the session is disposed (nothing raised).
    /// </summary>
    public bool ChangeDialect(LineTokenizer tokenizer)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        lock (_gate)
        {
            if (_disposed || ReferenceEquals(tokenizer, _tokenizer))
            {
                return false;
            }

            _tokenizer = tokenizer;
            _valid = 0;
            _settleFrom = _settleTo = -1;
        }

        Reclassified?.Invoke(this, EventArgs.Empty);
        return true;
    }

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
            if (_disposed)
            {
                return;
            }

            if (!ReferenceEquals(snapshot, _snapshot))
            {
                ClassifyAlone(snapshot, first, last, into);
                return;
            }

            Known(first);
            var tokenizer = _tokenizer;
            ulong state = first == 0 ? tokenizer.Initial : _ends[first - 1];
            for (int index = first; index <= last; index++)
            {
                var line = snapshot.GetLine(index);
                state = tokenizer.Tokenize(Read(snapshot, line, out var rented), line.Start, state, into);
                Return(rented);
                if (index == _valid)
                {
                    Learn(index, state);
                }
            }
        }
    }

    /// <inheritdoc />
    public LanguageRules? RulesAt(ITextSnapshot snapshot, int position)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        position = Math.Clamp(position, 0, snapshot.Length);
        int index = snapshot.GetLineIndex(position);
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            var tokenizer = _tokenizer;
            ulong state;
            if (ReferenceEquals(snapshot, _snapshot))
            {
                Known(index);
                state = index == 0 ? tokenizer.Initial : _ends[index - 1];
            }
            else
            {
                state = tokenizer.Initial;
                for (int above = 0; above < index; above++)
                {
                    var text = snapshot.GetLine(above);
                    state = tokenizer.Tokenize(Read(snapshot, text, out var held), text.Start, state, null);
                    Return(held);
                }
            }

            var line = snapshot.GetLine(index);
            var rules = tokenizer.RulesAt(Read(snapshot, line, out var rented), position - line.Start, state);
            Return(rented);
            return rules;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// X part 9, for a tokenizer that links tags (<see cref="LineTokenizer.LinksTags"/>: XML, XAML, MSBuild, Razor, HTML): the tag whose name holds <paramref name="position"/> is found in
    /// its line's tokens (a '&lt;' or '&lt;/' of markup and the name after it; C#'s '&lt;' is an operator, a comment's or a
    /// value's text is not a tag), then its partner by walking the lines' tokens from the states the session keeps,
    /// forward from a start tag or backward from an end tag, counting the elements between (an empty element, and the
    /// tokenizer's void elements, <see cref="LineTokenizer.IsVoidElement"/>, open none). Null when the name has no partner within <paramref name="maxLines"/> lines.
    /// </remarks>
    public LinkedEditingRanges? GetLinkedEditingRanges(ITextSnapshot snapshot, int position, int maxLines)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!_tokenizer.LinksTags || snapshot.Length == 0 || position < 0 || position > snapshot.Length)
        {
            return null;
        }

        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(snapshot, _snapshot))
            {
                return null;
            }

            using var spans = new TokenSpanBuffer(64);
            var tags = new List<MarkupTags.Tag>();
            int line = snapshot.GetLineIndex(position);
            LineTags(snapshot, line, spans, tags);
            int at = -1;
            for (int i = 0; i < tags.Count; i++)
            {
                if (tags[i].Kind != MarkupTags.Kind.SelfClose && tags[i].NameStart <= position && position <= tags[i].NameEnd)
                {
                    at = i;
                    break;
                }
            }

            if (at < 0)
            {
                return null;
            }

            var tag = tags[at];
            var tokenizer = _tokenizer;
            var own = new TextRange(tag.NameStart, tag.NameEnd - tag.NameStart);
            if (tag.Kind == MarkupTags.Kind.Open)
            {
                if (MarkupTags.IsVoid(tokenizer, snapshot, tag))
                {
                    return null;
                }

                var partner = Forward(snapshot, line, at, tags, maxLines, tokenizer, spans);
                return partner is { } found ? new LinkedEditingRanges([own, found]) : null;
            }

            var opener = Backward(snapshot, line, at, tags, maxLines, tokenizer, spans);
            return opener is { } open ? new LinkedEditingRanges([own, open]) : null;
        }
    }

    // From the start tag tags[at] of `line`: the name of the end tag that closes it.
    private TextRange? Forward(ITextSnapshot snapshot, int line, int at, List<MarkupTags.Tag> tags, int maxLines, LineTokenizer tokenizer, TokenSpanBuffer spans)
    {
        int depth = 0;
        int last = Math.Min(snapshot.LineCount - 1, line + Math.Max(0, maxLines));
        int i = at + 1;
        bool lastOpenVoid = false;
        for (int index = line; index <= last; index++)
        {
            if (index > line)
            {
                LineTags(snapshot, index, spans, tags);
                i = 0;
            }

            for (; i < tags.Count; i++)
            {
                var tag = tags[i];
                switch (tag.Kind)
                {
                    case MarkupTags.Kind.SelfClose:
                        if (lastOpenVoid)
                        {
                            lastOpenVoid = false;             // <br />: a void element opened none
                            break;
                        }

                        if (depth == 0)
                        {
                            return null;                      // the start tag asked about is an empty element
                        }

                        depth--;                              // an empty element opened none
                        break;
                    case MarkupTags.Kind.Open:
                        lastOpenVoid = MarkupTags.IsVoid(tokenizer, snapshot, tag);
                        if (!lastOpenVoid)
                        {
                            depth++;
                        }

                        break;
                    case MarkupTags.Kind.Close:
                        if (depth == 0)
                        {
                            return new TextRange(tag.NameStart, tag.NameEnd - tag.NameStart);
                        }

                        depth--;
                        break;
                }
            }
        }

        return null;
    }

    // From the end tag tags[at] of `line`: the name of the start tag it closes.
    private TextRange? Backward(ITextSnapshot snapshot, int line, int at, List<MarkupTags.Tag> tags, int maxLines, LineTokenizer tokenizer, TokenSpanBuffer spans)
    {
        int depth = 0;
        int first = Math.Max(0, line - Math.Max(0, maxLines));
        int i = at - 1;
        // Walking back: an empty element's "/>" comes before its start tag, so it marks that start tag as opening nothing.
        bool skipNextOpen = false;
        for (int index = line; index >= first; index--)
        {
            if (index < line)
            {
                LineTags(snapshot, index, spans, tags);
                i = tags.Count - 1;
            }

            for (; i >= 0; i--)
            {
                var tag = tags[i];
                switch (tag.Kind)
                {
                    case MarkupTags.Kind.SelfClose:
                        skipNextOpen = true;
                        break;
                    case MarkupTags.Kind.Close:
                        depth++;
                        break;
                    case MarkupTags.Kind.Open:
                        if (skipNextOpen)
                        {
                            skipNextOpen = false;
                            break;
                        }

                        if (MarkupTags.IsVoid(tokenizer, snapshot, tag))
                        {
                            break;
                        }

                        if (depth == 0)
                        {
                            return new TextRange(tag.NameStart, tag.NameEnd - tag.NameStart);
                        }

                        depth--;
                        break;
                }
            }
        }

        return null;
    }

    // The tag events of a line (its start and end tags' names, and the "/>" of empty elements), from its tokens.
    private void LineTags(ITextSnapshot snapshot, int index, TokenSpanBuffer spans, List<MarkupTags.Tag> tags)
    {
        Known(index);
        spans.Clear();
        tags.Clear();
        var tokenizer = _tokenizer;
        ulong state = index == 0 ? tokenizer.Initial : _ends[index - 1];
        var line = snapshot.GetLine(index);
        var text = Read(snapshot, line, out var rented);
        try
        {
            state = tokenizer.Tokenize(text, line.Start, state, spans);
            MarkupTags.Collect(text, line.Start, spans.Spans, tags);
        }
        finally
        {
            Return(rented);
        }

        if (index == _valid)
        {
            Learn(index, state);
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
        var tokenizer = _tokenizer;
        while (_valid < line)
        {
            int index = _valid;
            ulong state = index == 0 ? tokenizer.Initial : _ends[index - 1];
            var text = _snapshot.GetLine(index);
            state = tokenizer.Tokenize(Read(_snapshot, text, out var rented), text.Start, state, null);
            Return(rented);
            _scanned++;
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
        var tokenizer = _tokenizer;
        ulong state = tokenizer.Initial;
        for (int index = 0; index <= last; index++)
        {
            var line = snapshot.GetLine(index);
            state = tokenizer.Tokenize(Read(snapshot, line, out var rented), line.Start, state, index >= first ? into : null);
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
/// A language that colours by lines (<see cref="LineTokenizer"/>) and may format (<see cref="ITextFormatter"/>), each
/// buffer's session a <see cref="LineStateSession"/>: what a plug-in registers in <see cref="EditorLanguages"/> for a
/// language with no semantic layer (the C# plug-in's project languages, the JSON plug-in, the web languages). A subclass
/// picks a dialect per file (<see cref="TokenizerFor"/>: .tsx lexes JSX, .ts does not).
/// </summary>
public class LineStateLanguage : ILanguage
{
    /// <summary>A language of <paramref name="name"/> opening <paramref name="extensions"/>.</summary>
    /// <param name="name">Its name in <see cref="EditorLanguages"/>.</param>
    /// <param name="summary">What it is, in a line.</param>
    /// <param name="extensions">The extensions it opens, with their dot.</param>
    /// <param name="rules">Its comments, brackets and strings.</param>
    /// <param name="tokenizer">Its tokenizer (the default dialect).</param>
    /// <param name="formatter">Its formatter, or null.</param>
    public LineStateLanguage(string name, string summary, IReadOnlyList<string> extensions, LanguageRules rules, LineTokenizer tokenizer, ITextFormatter? formatter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(tokenizer);
        Name = name;
        Summary = summary;
        Extensions = extensions;
        Rules = rules;
        Tokenizer = tokenizer;
        Formatter = formatter;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Summary { get; }

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions { get; }

    /// <inheritdoc />
    public LanguageRules Rules { get; }

    /// <summary>Its tokenizer (the default dialect).</summary>
    public LineTokenizer Tokenizer { get; }

    /// <summary>Its formatter, or null.</summary>
    public ITextFormatter? Formatter { get; }

    /// <summary>Whether Format document formats its files.</summary>
    public bool Formats => Formatter is not null;

    /// <inheritdoc />
    public ILanguageSession Open(ITextBuffer buffer) => new LineStateSession(buffer, Tokenizer, Formatter, LanguageSettings.Default);

    /// <inheritdoc />
    public ILanguageSession Open(ITextBuffer buffer, LanguageOpenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return OpenSession(buffer, context);
    }

    /// <summary>The tokenizer (dialect) a file opens with: <see cref="Tokenizer"/> by default.</summary>
    protected virtual LineTokenizer TokenizerFor(LanguageOpenContext context) => Tokenizer;

    /// <summary>
    /// The session of a file that opens with <paramref name="context"/>: by default one with <see cref="TokenizerFor"/>'s
    /// dialect, <see cref="Formatter"/> and the context's settings. A subclass may pick the formatter per file (none for an
    /// indented dialect it cannot format) or keep the session to change its dialect when a version is learned later
    /// (<see cref="LineStateSession.ChangeDialect"/>, off the calling thread); it never waits here (the UI thread opens).
    /// </summary>
    protected virtual LineStateSession OpenSession(ITextBuffer buffer, LanguageOpenContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new LineStateSession(buffer, TokenizerFor(context), Formatter, context.Settings);
    }
}

/// <summary>
/// The tags of a markup line, read from its tokens (X part 9's linked tag rename): a markup '&lt;' or '&lt;/' (punctuation,
/// never a script's operator) with a name after it, and "/>". Any tokenizer that colours tags this way may link them
/// (<see cref="LineTokenizer.LinksTags"/>).
/// </summary>
public static class MarkupTags
{
    private static readonly string[] HtmlVoidElements = ["area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"];

    /// <summary>What a tag event is.</summary>
    public enum Kind
    {
        /// <summary>A start tag's name.</summary>
        Open,

        /// <summary>An end tag's name.</summary>
        Close,

        /// <summary>The "/>" that ends an empty element.</summary>
        SelfClose,
    }

    /// <summary>A tag event: its kind and its name's place (the "/>"'s for a self-close).</summary>
    public readonly record struct Tag(Kind Kind, int NameStart, int NameEnd);

    /// <summary>Adds the line's tag events in order: a markup '&lt;' or '&lt;/' (punctuation, not C#'s operator) with a name after it, and "/>".</summary>
    public static void Collect(ReadOnlySpan<char> text, int lineStart, ReadOnlySpan<TokenSpan> spans, List<Tag> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        foreach (var span in spans)
        {
            if (span.Class != TokenClasses.Punctuation)
            {
                continue;
            }

            int at = span.Start - lineStart;
            if (at < 0 || at + span.Length > text.Length)
            {
                continue;
            }

            var mark = text.Slice(at, span.Length);
            if (mark is "/>")
            {
                into.Add(new Tag(Kind.SelfClose, span.Start, span.End));
                continue;
            }

            bool open = mark is "<";
            if (!open && mark is not "</")
            {
                continue;
            }

            int name = at + span.Length;
            int end = name;
            while (end < text.Length && IsNameChar(text[end]))
            {
                end++;
            }

            if (end > name)
            {
                into.Add(new Tag(open ? Kind.Open : Kind.Close, lineStart + name, lineStart + end));
            }
        }
    }

    /// <summary>Whether the start tag names a void element of <paramref name="tokenizer"/> (HTML's br, img, input, …), which has no end tag.</summary>
    public static bool IsVoid(LineTokenizer tokenizer, ITextSnapshot snapshot, Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(snapshot);
        int length = tag.NameEnd - tag.NameStart;
        if (length > 32)
        {
            return false;
        }

        Span<char> name = stackalloc char[length];
        snapshot.CopyTo(tag.NameStart, name);
        return tokenizer.IsVoidElement(name);
    }

    /// <summary>Whether <paramref name="name"/> is one of HTML's void elements (ignoring case).</summary>
    public static bool IsHtmlVoid(ReadOnlySpan<char> name)
    {
        foreach (var known in HtmlVoidElements)
        {
            if (name.Equals(known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':';
}
