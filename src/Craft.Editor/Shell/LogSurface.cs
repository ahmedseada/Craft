// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;

namespace Craft.Editor.Shell;

/// <summary>
/// The bottom panel's Output as one read-only text (the owner's note 26): the host's log lines in one buffer, shown by
/// Craft's own editor in its read-only mode (<see cref="EditorModel.IsReadOnly"/>), so a drag selects across lines,
/// Shift+click extends, Ctrl+A, Ctrl+C, the context menu's Copy and Ctrl+F work as in any editor, and only the visible
/// lines are laid out and drawn. Lines are posted from any thread (<see cref="Post"/>) and appended on the UI thread in one
/// change per batch (<see cref="Flush"/>; the panel dispatches one flush however many lines came, never one dispatch per
/// line), outside any undo history; the oldest lines past <see cref="CraftOptions.HostLogLines"/> go in the same change.
/// The editor showing it (<see cref="Attach"/>) keeps its selection and its place: it follows the end while
/// <see cref="Following"/> (on until the user scrolls up, on again at the end or by the header's toggle), and otherwise
/// keeps showing the same lines when the oldest are trimmed. A <see cref="Filter"/> shows only the lines holding its text,
/// as a view over the same text (nothing copied). Used on the UI thread, except <see cref="Post"/>.
/// </summary>
public sealed class LogSurface : IDisposable
{
    private static readonly string NewLine = Environment.NewLine;
    private readonly PieceTableBuffer _buffer = new("");
    private readonly ReadOnlyTextDocument _document;
    private readonly Func<int> _maxLines;
    private readonly ConcurrentQueue<string> _pending = new();
    private readonly List<string> _batch = [];
    private readonly StringBuilder _text = new();
    private int _scheduled;
    private int _lines;
    private long _base;
    private LineFilterBuffer? _view;
    private ReadOnlyTextDocument? _viewDocument;
    private string _filter = "";
    private EditorModel? _model;
    private float _lastScrollY;
    private bool _following = true;

    /// <summary>An empty log keeping at most <paramref name="maxLines"/> lines (read at each flush; at least 1).</summary>
    public LogSurface(Func<int> maxLines)
    {
        ArgumentNullException.ThrowIfNull(maxLines);
        _maxLines = maxLines;
        _document = new ReadOnlyTextDocument(_buffer);
    }

    /// <summary>The whole log as a read-only document (its edits throw; the log changes through its buffer).</summary>
    public ITextDocument Document => _document;

    /// <summary>What the editor shows: <see cref="Document"/>, or the filter's view of it while <see cref="Filter"/> is set.</summary>
    public ITextDocument Shown => _viewDocument ?? _document;

    /// <summary>The log's lines (a posted line with line breaks counts each).</summary>
    public int LineCount => _lines;

    /// <summary>The lines <see cref="Shown"/> holds.</summary>
    public int ShownLineCount => _view is { } view ? view.Count : _lines;

    /// <summary>The language that colours the log's lines (errors, warnings, the time): internal to the shell, in no registry.</summary>
    public static ILanguage Language { get; } = new LogLanguage();

    /// <summary>
    /// The text a line must hold (ignoring case) to show; empty shows every line. A change makes a new
    /// <see cref="Shown"/> (raises <see cref="ShownChanged"/>): the panel shows it in a new editor and attaches that.
    /// </summary>
    public string Filter
    {
        get => _filter;
        set
        {
            value ??= "";
            if (string.Equals(value, _filter, StringComparison.Ordinal))
            {
                return;
            }

            _filter = value;
            _view = value.Length == 0 ? null : new LineFilterBuffer(value, _buffer.Current, _base);
            _viewDocument = _view is null ? null : new ReadOnlyTextDocument(_view);
            ShownChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Whether the editor follows the end as lines come: on at first; off when the user scrolls up, on again when they
    /// scroll back to the end; the header's toggle sets it (on scrolls to the end).
    /// </summary>
    public bool Following
    {
        get => _following;
        set
        {
            if (_following == value)
            {
                return;
            }

            _following = value;
            if (value && _model is { } model)
            {
                ScrollToEnd(model);
            }

            FollowingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised when <see cref="Shown"/> is another document (the filter changed).</summary>
    public event EventHandler? ShownChanged;

    /// <summary>Raised when <see cref="Following"/> changed (inside a frame too: a handler that changes the UI dispatches it).</summary>
    public event EventHandler? FollowingChanged;

    /// <summary>
    /// Queues <paramref name="line"/> (any thread). True when it is the first since the last <see cref="Flush"/>: the caller
    /// then schedules one flush on the UI thread; the lines posted until it runs join it.
    /// </summary>
    public bool Post(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _pending.Enqueue(line);
        return Interlocked.Exchange(ref _scheduled, 1) == 0;
    }

    /// <summary>
    /// On the UI thread: appends the posted lines in one change (with the oldest past the limit trimmed in it), updates
    /// the filter's view, and keeps the attached editor's selection and place (or follows the end). The lines appended.
    /// </summary>
    public int Flush()
    {
        Volatile.Write(ref _scheduled, 0);
        _batch.Clear();
        while (_pending.TryDequeue(out var line))
        {
            _batch.Add(line);
        }

        if (_batch.Count == 0)
        {
            return 0;
        }

        int added = Append(_batch);
        _batch.Clear();
        return added;
    }

    /// <summary>The editor that shows <see cref="Shown"/> (null when none): it is kept in place and followed.</summary>
    public void Attach(EditorModel? model)
    {
        if (ReferenceEquals(model, _model))
        {
            return;
        }

        if (_model is { } old)
        {
            old.ViewportChanged -= OnViewportChanged;
        }

        _model = model;
        if (model is not null)
        {
            _lastScrollY = model.ScrollY;
            model.ViewportChanged += OnViewportChanged;
            if (_following)
            {
                ScrollToEnd(model);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Attach(null);
        _pending.Clear();
    }

    // One change: the batch at the end (each line after a line break), and the oldest lines past the limit at the start.
    private int Append(List<string> entries)
    {
        var snapshot = _buffer.Current;
        int max = Math.Max(1, _maxLines());
        int added = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            string entry = entries[i];
            if (entry.AsSpan().ContainsAny('\r', '\n'))
            {
                entry = entry.ReplaceLineEndings(NewLine);
                entries[i] = entry;
            }

            added += 1 + Occurrences(entry, NewLine);
        }

        int total = _lines + added;
        int excess = Math.Max(0, total - max);
        int oldLength = snapshot.Length;
        int oldLines = _lines;
        TextEdit[] edits;
        int removedChars;
        int removedLines;
        int firstNew;
        _text.Clear();
        if (excess < oldLines || oldLines == 0 && excess == 0)
        {
            if (oldLines > 0)
            {
                _text.Append(NewLine);
            }

            Join(entries, 0);
            removedChars = excess == 0 ? 0 : snapshot.GetLine(excess).Start;
            removedLines = excess;
            var insert = TextEdit.Insert(oldLength, _text.ToString());
            edits = removedChars > 0 ? [TextEdit.Delete(0, removedChars), insert] : [insert];
            firstNew = oldLines - excess;
        }
        else
        {
            // The whole old text goes, and the first lines of the batch too: the batch's last lines replace it.
            Join(entries, excess - oldLines);
            removedChars = oldLength;
            removedLines = oldLines;
            edits = [new TextEdit(0, oldLength, _text.ToString())];
            firstNew = 0;
        }

        _text.Clear();
        var model = _model;
        var selection = model?.Selection ?? default;
        float scrollY = model?.ScrollY ?? 0;
        _buffer.Apply(edits);
        _lines = total - excess;
        _base += excess;
        if (_view is { } view)
        {
            (removedLines, removedChars) = view.Update(_buffer.Current, _base, firstNew);
        }

        if (model is not null)
        {
            // The appended text moved nothing; the trimmed text moved what came after it to the start.
            model.Select(Math.Max(0, selection.Anchor - removedChars), Math.Max(0, selection.Caret - removedChars));
            if (_following)
            {
                ScrollToEnd(model);
            }
            else if (removedLines > 0)
            {
                model.ScrollToOffsets(model.ScrollX, scrollY - (removedLines * model.Metrics.LineHeight), instant: true);
            }
        }

        return added;
    }

    // The entries joined by the line break, from their line `skip` on (the lines before it are dropped).
    private void Join(List<string> entries, int skip)
    {
        bool first = true;
        foreach (var entry in entries)
        {
            var text = entry.AsSpan();
            while (skip > 0)
            {
                int at = text.IndexOf(NewLine, StringComparison.Ordinal);
                if (at < 0)
                {
                    break;
                }

                text = text[(at + NewLine.Length)..];
                skip--;
            }

            if (skip > 0)
            {
                skip--;                                       // the entry's last (or only) line is dropped too
                continue;
            }

            if (!first)
            {
                _text.Append(NewLine);
            }

            _text.Append(text);
            first = false;
        }
    }

    private static int Occurrences(string text, string value)
    {
        int count = 0;
        int at = 0;
        while ((at = text.IndexOf(value, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += value.Length;
        }

        return count;
    }

    private static void ScrollToEnd(EditorModel model) =>
        model.ScrollToOffsets(model.ScrollX, model.ContentHeight - model.ViewportHeight, instant: true);

    // The user's scroll: up (away from the end) stops following, back at the end follows again. A frame drawn before the
    // view caught up with a follow's scroll repeats the last offset, never a smaller one.
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
            Following = true;
        }
        else if (y < _lastScrollY - 0.5f)
        {
            Following = false;
        }

        _lastScrollY = y;
    }
}

/// <summary>The log's line as Output shows it: "HH:mm:ss  Level        Source: message  (error)", and its level read back.</summary>
public static class LogText
{
    /// <summary>The column the level starts at (after the time and two spaces).</summary>
    public const int LevelColumn = 10;

    /// <summary><paramref name="entry"/> as one line (its message's own line breaks kept).</summary>
    public static string Line(EditorLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.Create(CultureInfo.InvariantCulture,
            $"{entry.Time.ToLocalTime():HH:mm:ss}  {entry.Level,-11}  {entry.Source}: {entry.Message}{(entry.Error is null ? "" : "  (" + entry.Error.Message + ")")}");
    }

    /// <summary>The level of a line <see cref="Line"/> made, or null for another line (a message's continuation).</summary>
    public static EditorLogLevel? LevelOf(ReadOnlySpan<char> line)
    {
        if (line.Length <= LevelColumn || line[2] != ':' || line[5] != ':' || line[8] != ' ')
        {
            return null;
        }

        var level = line[LevelColumn..];
        return level.StartsWith("Error", StringComparison.Ordinal) ? EditorLogLevel.Error
            : level.StartsWith("Warning", StringComparison.Ordinal) ? EditorLogLevel.Warning
            : level.StartsWith("Information", StringComparison.Ordinal) ? EditorLogLevel.Information
            : level.StartsWith("Debug", StringComparison.Ordinal) ? EditorLogLevel.Debug
            : null;
    }
}

/// <summary>The log's token classes and the theme that colours them from the theme's own error, warning and secondary colours.</summary>
public static class LogClasses
{
    /// <summary>An error's line.</summary>
    public const string Error = "log - error";

    /// <summary>A warning's line.</summary>
    public const string Warning = "log - warning";

    /// <summary>The time and a debug line: secondary text.</summary>
    public const string Quiet = "log - quiet";

    /// <summary>
    /// <paramref name="theme"/> for a read-only text on the panel's surface: the surface's colour behind the text (no box
    /// inside a box, no current line's band) and the log's classes in its error, warning and secondary colours.
    /// </summary>
    public static ITheme Theme(ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return theme is LogTheme log ? log : new LogTheme(theme);
    }

    private sealed class LogTheme : ITheme
    {
        private readonly ITheme _inner;

        public LogTheme(ITheme inner)
        {
            _inner = inner;
            var colors = inner.Colors;
            Colors = colors with { Background = colors.Surface, Gutter = colors.Surface, CurrentLine = colors.Surface };
        }

        public string Name => _inner.Name;

        public string Summary => _inner.Summary;

        public bool IsDark => _inner.IsDark;

        public EditorColors Colors { get; }

        public TextStyle StyleOf(string tokenClass) => tokenClass switch
        {
            Error => new TextStyle(Colors.Error),
            Warning => new TextStyle(Colors.Warning),
            Quiet => new TextStyle(Colors.SecondaryText),
            _ => _inner.StyleOf(tokenClass),
        };
    }
}

/// <summary>The log's language: no files, plain rules; a line's time is quiet, an error's or a warning's line coloured.</summary>
internal sealed class LogLanguage : ILanguage
{
    public string Name => "craft-log";

    public string Summary => "The host's log in Output (internal to the shell)";

    public IReadOnlyList<string> Extensions => [];

    public LanguageRules Rules => LanguageRules.Plain;

    public ILanguageSession Open(ITextBuffer buffer) => new Session();

    private sealed class Session : ILanguageSession
    {
        public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
        {
            int end = Math.Min(snapshot.Length, start + Math.Max(0, length));
            int first = snapshot.GetLineIndex(Math.Clamp(start, 0, snapshot.Length));
            int last = snapshot.GetLineIndex(end);
            char[]? buffer = null;
            try
            {
                for (int index = first; index <= last; index++)
                {
                    var line = snapshot.GetLine(index);
                    var text = TextReading.ReadLine(snapshot, line, ref buffer);
                    switch (LogText.LevelOf(text))
                    {
                        case EditorLogLevel.Error:
                            into.Add(line.Start, line.Length, LogClasses.Error);
                            break;
                        case EditorLogLevel.Warning:
                            into.Add(line.Start, line.Length, LogClasses.Warning);
                            break;
                        case EditorLogLevel.Debug:
                            into.Add(line.Start, line.Length, LogClasses.Quiet);
                            break;
                        case EditorLogLevel.Information:
                            into.Add(line.Start, 8, LogClasses.Quiet);
                            break;
                    }
                }
            }
            finally
            {
                TextReading.Return(buffer);
            }
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>A read-only document over a buffer: its edits throw (the read-only editor refuses them before), nothing to undo or save.</summary>
internal sealed class ReadOnlyTextDocument(ITextBuffer buffer) : ITextDocument
{
    public ITextBuffer Buffer => buffer;

    public string? FilePath => null;

    public bool IsDirty => false;

    public bool CanUndo => false;

    public bool CanRedo => false;

    public event EventHandler? StateChanged
    {
        add { }
        remove { }
    }

    public ITextSnapshot Edit(ReadOnlySpan<TextEdit> edits, EditKind kind, SelectionState before, SelectionState after) =>
        throw new InvalidOperationException("This text is read-only (the host's log): nothing edits it; its owner appends through its buffer.");

    public SelectionState? Undo() => null;

    public SelectionState? Redo() => null;

    public Task SaveAsync(string? path = null, CancellationToken cancellationToken = default) =>
        Task.FromException(new InvalidOperationException("This text is read-only (the host's log) and has no file: copy it instead."));

    public void Dispose()
    {
    }
}

/// <summary>
/// The lines of a source text that hold a filter's text, as a text of their own read through to the source's characters
/// (nothing copied): one entry per shown line (its absolute source line, its length, its absolute start in the view),
/// in arrays that only grow at their end, so each snapshot keeps reading the entries it was made with while new ones are
/// added; trimmed entries move the live range's start, and a full array is copied once into a new one, live entries only.
/// The view's line break is the system's.
/// </summary>
internal sealed class LineFilterBuffer : ITextBuffer
{
    private static readonly string NewLine = Environment.NewLine;
    private readonly string _filter;
    private long[] _source = new long[64];
    private int[] _length = new int[64];
    private long[] _start = new long[64];
    private int _first;
    private int _count;
    private long _next;
    private FilteredSnapshot _current;

    public LineFilterBuffer(string filter, ITextSnapshot source, long sourceBase)
    {
        _filter = filter;
        AppendMatches(source, sourceBase, 0);
        _current = new FilteredSnapshot(this, 0, source, sourceBase, _source, _length, _start, _first, _count);
    }

    public ITextSnapshot Current => _current;

    public int Count => _count - _first;

    public event EventHandler<TextChangedEventArgs>? Changed;

    public ITextSnapshot Apply(ReadOnlySpan<TextEdit> edits, TextChangeReason reason = TextChangeReason.Edit) =>
        throw new InvalidOperationException("The filter's view of the log is read-only.");

    // The source changed: its line 0 is now absolute line `sourceBase`, its lines from `firstNew` are new. Drops the
    // trimmed entries, adds the new matches, raises one change; the view's removed lines and characters.
    public (int Lines, int Characters) Update(ITextSnapshot source, long sourceBase, int firstNew)
    {
        var before = _current;
        int oldFirst = _first;
        int oldCount = _count;
        while (_first < _count && _source[_first] < sourceBase)
        {
            _first++;
        }

        int removed = _first - oldFirst;
        int removedChars = removed == 0 ? 0 : _first < oldCount ? (int)(_start[_first] - _start[oldFirst]) : before.Length;
        int kept = oldCount - _first;
        int from = kept > 0 ? (int)(_start[oldCount - 1] - _start[_first]) + _length[oldCount - 1] : 0;   // before a grow moves the entries
        int added = AppendMatches(source, sourceBase, Math.Max(0, firstNew));
        if (removed == 0 && added == 0)
        {
            return (0, 0);                                   // the view's text is the same: its snapshot still reads it right
        }

        var after = new FilteredSnapshot(this, before.Version + 1, source, sourceBase, _source, _length, _start, _first, _count);
        _current = after;

        TextEdit[] edits;
        if (added == 0)
        {
            edits = [TextEdit.Delete(0, removedChars)];
        }
        else
        {
            string text = after.GetText(from, after.Length - from);
            edits = kept == 0 && removedChars > 0
                ? [new TextEdit(0, before.Length, text)]
                : removedChars > 0 ? [TextEdit.Delete(0, removedChars), TextEdit.Insert(before.Length, text)] : [TextEdit.Insert(before.Length, text)];
        }

        Changed?.Invoke(this, new TextChangedEventArgs(before, after, edits, TextChangeReason.Edit));
        return (removed, removedChars);
    }

    private int AppendMatches(ITextSnapshot source, long sourceBase, int from)
    {
        int added = 0;
        char[]? buffer = null;
        try
        {
            for (int index = from; index < source.LineCount; index++)
            {
                var line = source.GetLine(index);
                var text = TextReading.ReadLine(source, line, ref buffer);
                if (!text.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (_count == _source.Length)
                {
                    Grow();
                }

                _source[_count] = sourceBase + index;
                _length[_count] = line.Length;
                _start[_count] = _next;
                _next += line.Length + NewLine.Length;
                _count++;
                added++;
            }
        }
        finally
        {
            TextReading.Return(buffer);
        }

        return added;
    }

    // New arrays (the snapshots made so far keep the old ones), the live entries at their start.
    private void Grow()
    {
        int live = _count - _first;
        int size = Math.Max(64, live * 2);
        var source = new long[size];
        var length = new int[size];
        var start = new long[size];
        Array.Copy(_source, _first, source, 0, live);
        Array.Copy(_length, _first, length, 0, live);
        Array.Copy(_start, _first, start, 0, live);
        _source = source;
        _length = length;
        _start = start;
        _first = 0;
        _count = live;
    }

    private sealed class FilteredSnapshot(LineFilterBuffer buffer, int version, ITextSnapshot source, long sourceBase,
        long[] sourceLines, int[] lengths, long[] starts, int first, int count) : ITextSnapshot
    {
        private readonly int _lines = count - first;

        public ITextBuffer Buffer => buffer;

        public int Version => version;

        public int Length => _lines == 0 ? 0 : (int)(starts[count - 1] - starts[first]) + lengths[count - 1];

        public int LineCount => Math.Max(1, _lines);

        public char this[int position]
        {
            get
            {
                if (position < 0 || position >= Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(position), position, $"The view holds {Length} characters.");
                }

                int entry = first + GetLineIndex(position);
                int column = position - (int)(starts[entry] - starts[first]);
                return column < lengths[entry] ? source[SourceStart(entry) + column] : NewLine[column - lengths[entry]];
            }
        }

        public TextLine GetLine(int index)
        {
            if (_lines == 0 && index == 0)
            {
                return new TextLine(0, 0, 0, 0);
            }

            if (index < 0 || index >= _lines)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, $"The view has {LineCount} lines.");
            }

            int entry = first + index;
            return new TextLine(index, (int)(starts[entry] - starts[first]), lengths[entry], index < _lines - 1 ? NewLine.Length : 0);
        }

        public int GetLineIndex(int position)
        {
            if (_lines == 0)
            {
                return 0;
            }

            long target = starts[first] + Math.Clamp(position, 0, Length);
            int low = first;
            int high = count - 1;
            while (low < high)
            {
                int middle = low + ((high - low + 1) >> 1);
                if (starts[middle] <= target)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return low - first;
        }

        public void CopyTo(int start, Span<char> destination)
        {
            if (destination.IsEmpty)
            {
                return;
            }

            if (start < 0 || start + destination.Length > Length)
            {
                throw new ArgumentOutOfRangeException(nameof(start), start, $"{destination.Length} characters from {start} run outside the view of {Length}.");
            }

            int index = GetLineIndex(start);
            int column = start - (int)(starts[first + index] - starts[first]);
            int written = 0;
            while (written < destination.Length)
            {
                int entry = first + index;
                int length = lengths[entry];
                if (column < length)
                {
                    int take = Math.Min(length - column, destination.Length - written);
                    source.CopyTo(SourceStart(entry) + column, destination.Slice(written, take));
                    written += take;
                    column += take;
                }

                while (written < destination.Length && column >= length && column < length + NewLine.Length)
                {
                    destination[written++] = NewLine[column - length];
                    column++;
                }

                index++;
                column = 0;
            }
        }

        public string GetText(int start, int length) => string.Create(length, (Snapshot: this, Start: start), static (span, state) => state.Snapshot.CopyTo(state.Start, span));

        private int SourceStart(int entry) => source.GetLine((int)(sourceLines[entry] - sourceBase)).Start;
    }
}

/// <summary>
/// The keys of a read-only text (Output): its editor's own keys first (moving, selecting, Ctrl+A, Ctrl+C, Ctrl+F, F3, its
/// edits refused), then the window's (the key bindings, the menus) with no text editor as the target, so a binding of
/// the active document's commands never acts on the read-only text and Ctrl+J or Ctrl+` still work from it.
/// </summary>
public sealed class ReadOnlySurfaceKeys(Func<Craft.Editor.Abstractions.IEditorKeyHandler?> window) : Craft.Editor.Abstractions.IEditorKeyHandler
{
    /// <inheritdoc />
    public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor) =>
        (editor?.OnKey(key, modifiers) ?? false) || (window()?.HandleKey(key, modifiers, null) ?? false);
}
