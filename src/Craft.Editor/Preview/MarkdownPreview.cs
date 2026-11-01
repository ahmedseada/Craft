// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Runtime.InteropServices;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Text;
using Craft.Editor.Shell;

namespace Craft.Editor.Preview;

/// <summary>What a click on the preview's text does: open a link (an address, a file of the workspace, an image) or copy a code block.</summary>
/// <param name="Kind">"link" or "copy".</param>
/// <param name="Text">The link's target as written, or the code to copy.</param>
public sealed record PreviewAction(string Kind, string Text);

/// <summary>
/// A Markdown document rendered for its preview tab (X part 9, item 19), with the chat's Markdown renderer (X part 10's
/// <c>TranscriptWriter</c>: headings, lists, quotes, tables, rules, links, inline code, emphasis, fenced code coloured by its
/// language, an image a link that opens it): one read-only text shown by Craft's own editor surface (only the shown lines
/// laid out and drawn), its spans and code fills its own. The source is rendered again after a pause
/// (<c>CRAFT_MARKDOWN_PREVIEW_DELAY_MS</c>) off the UI thread; each line knows its source's line, so the preview follows
/// the source's scroll (<see cref="Follow"/>). Used on the UI thread; the rendering runs on the thread pool.
/// </summary>
public sealed class MarkdownPreview : IDisposable
{
    private readonly PieceTableBuffer _buffer = new("");
    private readonly ReadOnlyTextDocument _document;
    private readonly Action<Action> _post;
    private readonly Func<CraftOptions> _options;
    private readonly PreviewLanguage _language;
    private readonly PreviewBackdrop _backdrop;
    private readonly Action _raise;
    private Rendered _shown = Rendered.Empty;
    private IEditorDocument? _source;
    private EditorHost? _host;
    private CancellationTokenSource? _pending;
    private TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string _lastText = "";
    private int _columns = 80;
    private int _generation;
    private bool _disposed;

    /// <summary>A preview whose rendered text comes back through <paramref name="post"/> (the UI thread's dispatcher).</summary>
    public MarkdownPreview(Action<Action> post, Func<CraftOptions> options)
    {
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _document = new ReadOnlyTextDocument(_buffer);
        _language = new PreviewLanguage(this);
        _backdrop = new PreviewBackdrop(this);
        _raise = _language.Raise;
        _settled.TrySetResult();
    }

    /// <summary>The rendered text as a read-only document.</summary>
    public ITextDocument Document => _document;

    /// <summary>The language that colours it (its own spans; code by the code's language).</summary>
    public ILanguage Language => _language;

    /// <summary>The fills behind its code blocks.</summary>
    public ILineBackdrop Backdrop => _backdrop;

    /// <summary>The source document (its file's tab), or null.</summary>
    public IEditorDocument? Source => _source;

    /// <summary>The source's file, or null.</summary>
    public string? SourcePath => _source?.FilePath;

    /// <summary>The colours its fills take (the theme's).</summary>
    public EditorColors Colors { get; set; } = EditorThemes.Get("dark").Colors;

    /// <summary>How many renders landed.</summary>
    public int Renders { get; private set; }

    /// <summary>Completes when no render is pending (the last edit's has landed).</summary>
    public Task Settled => _settled.Task;

    /// <summary>Raised (on the UI thread) after a render landed.</summary>
    public event EventHandler? Updated;

    /// <summary>The columns the text wraps at (the view's width in cells); a change renders again.</summary>
    public int Columns
    {
        get => _columns;
        set
        {
            value = Math.Max(20, value);
            if (value != _columns)
            {
                _columns = value;
                Schedule(_lastText, 0);
            }
        }
    }

    /// <summary>The theme the preview's view draws with: <paramref name="theme"/> with the renderer's own classes.</summary>
    public static ITheme Theme(ITheme theme) => ChatClasses.Theme(theme);

    /// <summary>Follows <paramref name="source"/> of <paramref name="host"/>: rendered now and again after each change's pause.</summary>
    public void Attach(EditorHost host, IEditorDocument source)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(source);
        Detach();
        _host = host;
        _source = source;
        host.DocumentChanged += OnDocumentChanged;
        var snapshot = source.Document.Buffer.Current;
        Schedule(snapshot.GetText(0, snapshot.Length), 0);
    }

    /// <summary>Renders <paramref name="markdown"/> now (off the UI thread); completes when it landed.</summary>
    public Task RenderAsync(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        Schedule(markdown, 0);
        return Settled;
    }

    /// <summary>The preview's first line written from the source's line <paramref name="sourceLine"/> (or the nearest before it), from 0.</summary>
    public int PreviewLineOf(int sourceLine)
    {
        var lines = _shown.Lines;
        int found = 0;
        int low = 0, high = lines.Length - 1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            if (lines[middle].SourceLine <= sourceLine)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        // The first of the lines written from that source line.
        while (found > 0 && lines[found - 1].SourceLine == lines[found].SourceLine)
        {
            found--;
        }

        return found;
    }

    /// <summary>The source's line the preview's line <paramref name="previewLine"/> was written from (0 when not known).</summary>
    public int SourceLineOf(int previewLine)
    {
        var lines = _shown.Lines;
        return previewLine >= 0 && previewLine < lines.Length ? Math.Max(0, lines[previewLine].SourceLine) : 0;
    }

    /// <summary>
    /// Scrolls <paramref name="preview"/> (the preview's editor) to the place of <paramref name="source"/>'s top line
    /// (<c>CRAFT_MARKDOWN_PREVIEW_FOLLOW</c>); false when it does not follow or nothing moved.
    /// </summary>
    public bool Follow(EditorModel source, EditorModel preview)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(preview);
        if (!_options().MarkdownPreviewFollow || source.Metrics.LineHeight <= 0)
        {
            return false;
        }

        int top = source.LineAtRow((int)(source.ScrollY / source.Metrics.LineHeight));
        float y = preview.RowOfLine(Math.Min(PreviewLineOf(top), Math.Max(0, preview.Snapshot.LineCount - 1))) * preview.Metrics.LineHeight;
        if (Math.Abs(y - preview.ScrollY) < 0.5f)
        {
            return false;
        }

        preview.ScrollToOffsets(preview.ScrollX, y, instant: true);
        return true;
    }

    /// <summary>What a click at <paramref name="position"/> of the rendered text does (a link, a code block's Copy), or null.</summary>
    public PreviewAction? ActionAt(int position)
    {
        var snapshot = _buffer.Current;
        if (position < 0 || position > snapshot.Length || _shown.Lines.Length == 0)
        {
            return null;
        }

        int index = snapshot.GetLineIndex(position);
        if (index >= _shown.Lines.Length)
        {
            return null;
        }

        int column = position - snapshot.GetLine(index).Start;
        ref readonly var line = ref _shown.Lines[index];
        for (int i = line.ActionStart; i < line.ActionStart + line.ActionCount; i++)
        {
            var action = _shown.Actions[i];
            if (column >= action.Column && column < action.Column + action.Length)
            {
                return action.Action.Kind switch
                {
                    TranscriptActionKind.Link when action.Action.Text is { } url => new PreviewAction("link", url),
                    TranscriptActionKind.CopyCode when action.Action.Text is { } code => new PreviewAction("copy", code),
                    _ => null,
                };
            }
        }

        return null;
    }

    /// <summary>
    /// The full path a link of the preview names, relative to the source's folder (an image, another document), or null for
    /// an address (http, https, mailto) or a heading's anchor.
    /// </summary>
    public string? FileOf(string link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (link.Length == 0 || link[0] == '#' || link.Contains("://", StringComparison.Ordinal) || link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int hash = link.IndexOf('#', StringComparison.Ordinal);
        string path = Uri.UnescapeDataString(hash > 0 ? link[..hash] : link);
        string folder = SourcePath is { } source ? Path.GetDirectoryName(source) ?? "" : Environment.CurrentDirectory;
        try
        {
            return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar), folder);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Detach();
        _pending?.Cancel();
        _pending?.Dispose();
        _shown.Dispose();
        _settled.TrySetResult();
    }

    private void Detach()
    {
        if (_host is { } host)
        {
            host.DocumentChanged -= OnDocumentChanged;
        }

        _host = null;
        _source = null;
    }

    private void OnDocumentChanged(object? sender, EditorDocumentChangedEventArgs e)
    {
        if (ReferenceEquals(e.Document, _source))
        {
            var snapshot = e.Change.After;
            Schedule(snapshot.GetText(0, snapshot.Length), _options().MarkdownPreviewDelayMilliseconds);
        }
    }

    // A render after a pause (the next change cancels it), on the thread pool; it lands on the UI thread.
    private void Schedule(string markdown, int delayMilliseconds)
    {
        if (_disposed)
        {
            return;
        }

        _lastText = markdown;
        _pending?.Cancel();
        _pending?.Dispose();
        var cancel = _pending = new CancellationTokenSource();
        if (_settled.Task.IsCompleted)
        {
            _settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var settled = _settled;
        int generation = ++_generation;
        int columns = _columns;
        var token = cancel.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                if (delayMilliseconds > 0)
                {
                    await Task.Delay(delayMilliseconds, token).ConfigureAwait(false);
                }

                var rendered = Build(markdown, columns);
                if (token.IsCancellationRequested)
                {
                    rendered.Dispose();
                    return;
                }

                _post(() => Land(rendered, generation, settled));
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private void Land(Rendered rendered, int generation, TaskCompletionSource settled)
    {
        if (_disposed || generation != _generation)
        {
            rendered.Dispose();
            return;
        }

        var old = _shown;
        _shown = rendered;
        var before = _buffer.Current;
        _buffer.Apply([new TextEdit(0, before.Length, rendered.Text)]);
        old.Dispose();
        Renders++;
        _raise();
        Updated?.Invoke(this, EventArgs.Empty);
        settled.TrySetResult();
    }

    // The writer's lines, spans (in order, without overlaps), actions and code blocks for markdown at columns.
    private static Rendered Build(string markdown, int columns)
    {
        var writer = new TranscriptWriter { Columns = columns, FoldLines = 0, Preview = true, SourceLine = 0 };
        writer.Markdown(0, markdown, colour: true, static _ => true);
        var lines = writer.Lines.ToArray();
        var spans = writer.Spans;
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

        // A line written before any source line (none) keeps 0: the top.
        for (int l = 0; l < lines.Length; l++)
        {
            if (lines[l].SourceLine < 0)
            {
                lines[l].SourceLine = l > 0 ? lines[l - 1].SourceLine : 0;
            }
        }

        return new Rendered(writer.Text.ToString(), lines, [.. ordered], [.. writer.Actions], [.. writer.Blocks]);
    }

    // ---- what a frame reads ----

    internal void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into)
    {
        var shown = _shown;
        if (!ReferenceEquals(snapshot, _buffer.Current) || shown.Lines.Length == 0)
        {
            return;
        }

        int end = Math.Min(snapshot.Length, start + Math.Max(0, length));
        int first = snapshot.GetLineIndex(Math.Clamp(start, 0, snapshot.Length));
        int last = Math.Min(snapshot.GetLineIndex(end), shown.Lines.Length - 1);
        for (int index = first; index <= last; index++)
        {
            ref readonly var row = ref shown.Lines[index];
            int at = snapshot.GetLine(index).Start;
            if (row.Block >= 0)
            {
                var block = shown.Blocks[row.Block];
                var code = block.Spans(_post, _raise);
                int from = row.BlockOffset;
                int to = from + row.Length - row.Indent;
                foreach (var span in code)
                {
                    if (span.End <= from)
                    {
                        continue;
                    }

                    if (span.Start >= to)
                    {
                        break;
                    }

                    int s = Math.Max(span.Start, from);
                    int e = Math.Min(span.End, to);
                    into.Add(at + row.Indent + (s - from), e - s, span.Class);
                }

                continue;
            }

            for (int i = row.SpanStart; i < row.SpanStart + row.SpanCount; i++)
            {
                var span = shown.Spans[i];
                into.Add(at + span.Column, span.Length, span.Class);
            }
        }
    }

    internal void DrawBackdrop(IEditorCanvas canvas, int line, float left, float top, float cell, float height, float viewportWidth)
    {
        var shown = _shown;
        if (line < 0 || line >= shown.Lines.Length)
        {
            return;
        }

        ref readonly var row = ref shown.Lines[line];
        if (row.BoxWidth <= 0)
        {
            return;
        }

        var colors = Colors;
        float x = left + (row.BoxLeft * cell);
        float width = Math.Max(0, viewportWidth - x - cell);
        canvas.FillRect(x, top, width, height, ChatClasses.Mix(colors.Ground, colors.Surface, 0.7));
        if (row.Kind == TranscriptLineKind.CodeHeader)
        {
            canvas.FillRect(x, top + height - 1, width, 1, colors.Border);
        }
    }

    // One render: the text, its lines, spans, actions and code blocks (the blocks' colours made lazily, disposed with it).
    private sealed class Rendered(string text, TranscriptLine[] lines, LineSpan[] spans, LineAction[] actions, ChatCodeBlock[] blocks) : IDisposable
    {
        public static Rendered Empty { get; } = new("", [], [], [], []);

        public string Text { get; } = text;

        public TranscriptLine[] Lines { get; } = lines;

        public LineSpan[] Spans { get; } = spans;

        public LineAction[] Actions { get; } = actions;

        public ChatCodeBlock[] Blocks { get; } = blocks;

        public void Dispose()
        {
            foreach (var block in Blocks)
            {
                block.Dispose();
            }
        }
    }

    private sealed class PreviewBackdrop(MarkdownPreview owner) : ILineBackdrop
    {
        public void DrawLine(IEditorCanvas canvas, int line, float left, float top, float cellWidth, float height, float viewportWidth) =>
            owner.DrawBackdrop(canvas, line, left, top, cellWidth, height, viewportWidth);
    }

    // The preview's language: its own spans, in no registry.
    private sealed class PreviewLanguage(MarkdownPreview owner) : ILanguage
    {
        private readonly List<Session> _sessions = [];

        public string Name => "craft-markdown-preview";

        public string Summary => "A Markdown document's preview (internal to the shell)";

        public IReadOnlyList<string> Extensions => [];

        public LanguageRules Rules => LanguageRules.Plain;

        public ILanguageSession Open(ITextBuffer buffer)
        {
            var session = new Session(owner, this);
            _sessions.Add(session);
            return session;
        }

        public void Raise()
        {
            foreach (var session in CollectionsMarshal.AsSpan(_sessions).ToArray())
            {
                session.Raise();
            }
        }

        public sealed class Session(MarkdownPreview owner, PreviewLanguage language) : ILanguageSession
        {
            public event EventHandler? Reclassified;

            public void Raise() => Reclassified?.Invoke(this, EventArgs.Empty);

            public void Classify(ITextSnapshot snapshot, int start, int length, TokenSpanBuffer into) => owner.Classify(snapshot, start, length, into);

            public void Dispose() => language._sessions.Remove(this);
        }
    }
}
