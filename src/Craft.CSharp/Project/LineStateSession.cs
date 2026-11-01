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
    /// <remarks>
    /// X part 9, for markup (XML, XAML, MSBuild, Razor): the tag whose name holds <paramref name="position"/> is found in
    /// its line's tokens (a '&lt;' or '&lt;/' of markup and the name after it; C#'s '&lt;' is an operator, a comment's or a
    /// value's text is not a tag), then its partner by walking the lines' tokens from the states the session keeps,
    /// forward from a start tag or backward from an end tag, counting the elements between (an empty element, and HTML's
    /// void elements in Razor, open none). Null when the name has no partner within <paramref name="maxLines"/> lines.
    /// </remarks>
    public LinkedEditingRanges? GetLinkedEditingRanges(ITextSnapshot snapshot, int position, int maxLines)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_tokenizer is not MarkupTokenizer markup || snapshot.Length == 0 || position < 0 || position > snapshot.Length)
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
            bool razor = markup.Dialect == MarkupDialect.Razor;
            var own = new TextRange(tag.NameStart, tag.NameEnd - tag.NameStart);
            if (tag.Kind == MarkupTags.Kind.Open)
            {
                if (razor && MarkupTags.IsVoid(snapshot, tag))
                {
                    return null;
                }

                var partner = Forward(snapshot, line, at, tags, maxLines, razor, spans);
                return partner is { } found ? new LinkedEditingRanges([own, found]) : null;
            }

            var opener = Backward(snapshot, line, at, tags, maxLines, razor, spans);
            return opener is { } open ? new LinkedEditingRanges([own, open]) : null;
        }
    }

    // From the start tag tags[at] of `line`: the name of the end tag that closes it.
    private TextRange? Forward(ITextSnapshot snapshot, int line, int at, List<MarkupTags.Tag> tags, int maxLines, bool razor, TokenSpanBuffer spans)
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
                        lastOpenVoid = razor && MarkupTags.IsVoid(snapshot, tag);
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
    private TextRange? Backward(ITextSnapshot snapshot, int line, int at, List<MarkupTags.Tag> tags, int maxLines, bool razor, TokenSpanBuffer spans)
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

                        if (razor && MarkupTags.IsVoid(snapshot, tag))
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
        ulong state = index == 0 ? _tokenizer.Initial : _ends[index - 1];
        var line = snapshot.GetLine(index);
        var text = Read(snapshot, line, out var rented);
        try
        {
            state = _tokenizer.Tokenize(text, line.Start, state, spans);
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

/// <summary>The tags of a markup line, read from its tokens (X part 9's linked tag rename).</summary>
internal static class MarkupTags
{
    private static readonly string[] VoidElements = ["area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"];

    /// <summary>What a tag event is.</summary>
    internal enum Kind
    {
        /// <summary>A start tag's name.</summary>
        Open,

        /// <summary>An end tag's name.</summary>
        Close,

        /// <summary>The "/>" that ends an empty element.</summary>
        SelfClose,
    }

    /// <summary>A tag event: its kind and its name's place (the "/>"'s for a self-close).</summary>
    internal readonly record struct Tag(Kind Kind, int NameStart, int NameEnd);

    /// <summary>Adds the line's tag events in order: a markup '&lt;' or '&lt;/' (punctuation, not C#'s operator) with a name after it, and "/>".</summary>
    public static void Collect(ReadOnlySpan<char> text, int lineStart, ReadOnlySpan<TokenSpan> spans, List<Tag> into)
    {
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

    /// <summary>Whether the start tag names one of HTML's void elements (br, img, input, …), which have no end tag.</summary>
    public static bool IsVoid(ITextSnapshot snapshot, Tag tag)
    {
        int length = tag.NameEnd - tag.NameStart;
        if (length > 6)
        {
            return false;
        }

        Span<char> name = stackalloc char[length];
        snapshot.CopyTo(tag.NameStart, name);
        foreach (var known in VoidElements)
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
