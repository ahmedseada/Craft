// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Text;
using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>The token classes of the conversation's transcript (X part 10), drawn by <see cref="ChatClasses.Theme"/>.</summary>
public static class ChatClasses
{
    /// <summary>Quiet text: a message's speaker and model, a note, a step's details, the metrics.</summary>
    public const string Quiet = "chat - quiet";

    /// <summary>A heading.</summary>
    public const string Heading = "chat - heading";

    /// <summary>Bold text.</summary>
    public const string Bold = "chat - bold";

    /// <summary>Italic text.</summary>
    public const string Italic = "chat - italic";

    /// <summary>Inline code.</summary>
    public const string InlineCode = "chat - code";

    /// <summary>A link.</summary>
    public const string Link = "chat - link";

    /// <summary>An action ("Copy", "Apply as diff") on the message under the pointer, or always shown (a code block's, a step's).</summary>
    public const string Action = "chat - action";

    /// <summary>A message's own actions while the pointer is elsewhere: barely there (shown on hover).</summary>
    public const string ActionIdle = "chat - action - idle";

    /// <summary>A primary action (the accent).</summary>
    public const string Primary = "chat - action - primary";

    /// <summary>Something that failed.</summary>
    public const string Error = "chat - error";

    /// <summary>Something that succeeded (a tool's check mark, lines added).</summary>
    public const string Success = "chat - success";

    /// <summary>Something waiting for the user (a command to approve).</summary>
    public const string Waiting = "chat - waiting";

    /// <summary>
    /// <paramref name="theme"/> for the transcript: the panel's surface behind the text (no box inside a box, no current
    /// line's band) and the chat's classes in the theme's colours.
    /// </summary>
    public static ITheme Theme(ITheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return theme is ChatTheme chat ? chat : new ChatTheme(theme);
    }

    /// <summary><paramref name="argb"/> mixed with <paramref name="ground"/>: <paramref name="share"/> of the colour (0-1).</summary>
    public static uint Mix(uint argb, uint ground, double share)
    {
        static uint Channel(uint a, uint b, int shift, double share) =>
            (uint)Math.Clamp(Math.Round((((a >> shift) & 0xFF) * share) + (((b >> shift) & 0xFF) * (1 - share))), 0, 255) << shift;

        return 0xFF000000u | Channel(argb, ground, 16, share) | Channel(argb, ground, 8, share) | Channel(argb, ground, 0, share);
    }

    private sealed class ChatTheme : ITheme
    {
        private readonly ITheme _inner;
        private readonly TextStyle _quiet;
        private readonly TextStyle _heading;
        private readonly TextStyle _bold;
        private readonly TextStyle _italic;
        private readonly TextStyle _code;
        private readonly TextStyle _link;
        private readonly TextStyle _action;
        private readonly TextStyle _idle;
        private readonly TextStyle _primary;
        private readonly TextStyle _error;
        private readonly TextStyle _success;
        private readonly TextStyle _waiting;

        public ChatTheme(ITheme inner)
        {
            _inner = inner;
            var colors = inner.Colors;
            Colors = colors with { Background = colors.Surface, Gutter = colors.Surface, CurrentLine = colors.Surface };
            _quiet = new TextStyle(colors.SecondaryText);
            _heading = new TextStyle(colors.Text, Bold: true);
            _bold = new TextStyle(colors.Text, Bold: true);
            _italic = new TextStyle(colors.Text, Italic: true);
            _code = new TextStyle(colors.Accent);
            _link = new TextStyle(colors.Info);
            _action = new TextStyle(colors.Info);
            _idle = new TextStyle(Mix(colors.SecondaryText, colors.Surface, 0.28));
            _primary = new TextStyle(colors.Accent, Bold: true);
            _error = new TextStyle(colors.Error);
            _success = new TextStyle(colors.Success);
            _waiting = new TextStyle(colors.Warning, Bold: true);
        }

        public string Name => _inner.Name;

        public string Summary => _inner.Summary;

        public bool IsDark => _inner.IsDark;

        public EditorColors Colors { get; }

        public TextStyle StyleOf(string tokenClass) => tokenClass switch
        {
            Quiet => _quiet,
            Heading => _heading,
            Bold => _bold,
            Italic => _italic,
            InlineCode => _code,
            Link => _link,
            Action => _action,
            ActionIdle => _idle,
            Primary => _primary,
            Error => _error,
            Success => _success,
            Waiting => _waiting,
            _ => _inner.StyleOf(tokenClass),
        };
    }
}

/// <summary>What a line of the transcript is: content (copied) or the conversation's chrome (never copied as text).</summary>
public enum TranscriptLineKind
{
    /// <summary>A message's speaker or model line, with its actions (chrome).</summary>
    Meta,

    /// <summary>A line of a message's text.</summary>
    Body,

    /// <summary>A line of the user's message, in its bubble at the right.</summary>
    Bubble,

    /// <summary>A code block's header: its language and actions (chrome).</summary>
    CodeHeader,

    /// <summary>A line of a code block.</summary>
    Code,

    /// <summary>"⋯ 40 more lines · Show all" under a folded code block (chrome).</summary>
    Fold,

    /// <summary>A finished reply's metrics (chrome).</summary>
    Footer,

    /// <summary>A step of the agent's timeline (a tool call, a command, a file).</summary>
    Step,

    /// <summary>A line of a step's opened details (arguments, output).</summary>
    StepDetail,

    /// <summary>An entry's actions in a line of their own (chrome).</summary>
    Actions,

    /// <summary>The blank between messages (chrome).</summary>
    Gap,
}

/// <summary>What an action of the transcript does when clicked.</summary>
public enum TranscriptActionKind
{
    /// <summary>Copies the message (in <c>CRAFT_CHAT_COPY_FORMAT</c>).</summary>
    CopyMessage,

    /// <summary>Opens the message's menu.</summary>
    MessageMenu,

    /// <summary>Copies a code block.</summary>
    CopyCode,

    /// <summary>Proposes a code block at the active document's caret (a diff to accept).</summary>
    InsertCode,

    /// <summary>Types a shell block into the terminal and runs it.</summary>
    RunCode,

    /// <summary>Shows a folded code block whole, or folds it again.</summary>
    ShowAll,

    /// <summary>Opens or closes a step of the timeline.</summary>
    Toggle,

    /// <summary>Runs one of the entry's own actions (<see cref="PanelAction"/>).</summary>
    Panel,

    /// <summary>Opens a link.</summary>
    Link,

    /// <summary>Puts the user's message back in the composer to edit and send again.</summary>
    Edit,
}

/// <summary>An action placed in the transcript's text (a clickable label).</summary>
/// <param name="Kind">What it does.</param>
/// <param name="Entry">The panel entry it belongs to.</param>
public sealed record TranscriptAction(TranscriptActionKind Kind, int Entry)
{
    /// <summary>The code a code action works on, a link's address, or null.</summary>
    public string? Text { get; init; }

    /// <summary>The entry's action a <see cref="TranscriptActionKind.Panel"/> runs.</summary>
    public PanelAction? Action { get; init; }

    /// <summary>A code block's or a step's key for <see cref="TranscriptActionKind.ShowAll"/> and <see cref="TranscriptActionKind.Toggle"/>.</summary>
    public long Key { get; init; }
}

/// <summary>One line of a rendered message: its kind, its place in the message's text, its decoration and its spans.</summary>
internal struct TranscriptLine
{
    public TranscriptLineKind Kind;
    public int Start;          // within the message's text
    public int Length;         // without the line break
    public int Indent;         // decoration columns at its start (the bubble's padding, a code block's margin, a bullet's room)
    public int SpanStart;
    public int SpanCount;
    public int ActionStart;
    public int ActionCount;
    public int Block;          // the code block of a Code line, else -1
    public int BlockOffset;    // where in the block's text the line's code starts
    public int BoxLeft;        // a bubble's or code block's fill: its first column and its width (0: none)
    public int BoxWidth;
    public bool Continuation;  // a wrapped line's next part (joined to the line before by a space when copied)
    public int SourceLine;     // the Markdown source's line it was written from (X part 9's preview follows the scroll with it), or -1
}

/// <summary>A span of a line, by column within the line.</summary>
internal readonly record struct LineSpan(int Column, int Length, string Class);

/// <summary>An action placed on a line, by column.</summary>
internal readonly record struct LineAction(int Column, int Length, TranscriptAction Action);

/// <summary>A code block of a message: its language, its text as shown (tabs expanded), and its colours once classified.</summary>
internal sealed class ChatCodeBlock(string language, string code, bool colour) : IDisposable
{
    private static readonly string[] Shells = ["sh", "bash", "shell", "zsh", "console", "powershell", "pwsh", "ps1", "cmd", "bat", "terminal"];
    private ILanguageSession? _session;
    private ITextBuffer? _buffer;
    private TokenSpan[]? _spans;
    private bool _requested;
    private Action? _reclassified;

    public string Language { get; } = language;

    public string Code { get; } = code;

    public bool IsShell => Array.IndexOf(Shells, Language.ToLowerInvariant()) >= 0;

    // The block's colours (the language plug-in's tokenizer over its text), opened the first time a line of it is drawn;
    // empty until the language's parse lands (then `reclassified` runs on the UI thread through `post`).
    public ReadOnlySpan<TokenSpan> Spans(Action<Action> post, Action reclassified)
    {
        if (_spans is not null)
        {
            return _spans;
        }

        if (!colour || _requested)
        {
            return default;
        }

        _requested = true;
        Open(post, reclassified);           // apart: a lambda here would make its closure at every call (rule 63)
        return _spans ?? default;
    }

    private void Open(Action<Action> post, Action reclassified)
    {
        if (LanguageOf(Language) is not { } language)
        {
            return;
        }

        try
        {
            _buffer = new Craft.Core.Text.PieceTableBuffer(Code);
            _session = language.Open(_buffer);
            _reclassified = reclassified;
            _session.Reclassified += (_, _) => post(() =>
            {
                if (_session is not null && Compute())
                {
                    _reclassified?.Invoke();
                }
            });
            Compute();
        }
#pragma warning disable CA1031 // A language that cannot classify a chat's block leaves it plain.
        catch (Exception)
#pragma warning restore CA1031
        {
            _session = null;
        }
    }

    private bool Compute()
    {
        if (_session is not { } session || _buffer is not { } buffer)
        {
            return false;
        }

        var snapshot = buffer.Current;
        if (!session.WaitUntilClassified(snapshot, TimeSpan.Zero))
        {
            return false;
        }

        using var into = new TokenSpanBuffer();
        session.Classify(snapshot, 0, snapshot.Length, into);
        _spans = into.Spans.ToArray();
        return true;
    }

    /// <summary>The language a fence names ("csharp", "cs", "c#", "js"), by name, then by a file of that extension.</summary>
    internal static ILanguage? LanguageOf(string fence)
    {
        if (fence.Length == 0)
        {
            return null;
        }

        string name = fence.ToLowerInvariant() switch
        {
            "csharp" or "c#" or "cs" => "cs",
            "javascript" or "js" or "jsx" => "js",
            "typescript" or "ts" or "tsx" => "ts",
            "python" or "py" => "py",
            "shell" or "bash" or "sh" or "zsh" => "sh",
            "powershell" or "pwsh" or "ps1" => "ps1",
            "markdown" or "md" => "md",
            var other => other,
        };
        return EditorLanguages.Find(fence) ?? EditorLanguages.ForFile("chat." + name);
    }

    public void Dispose()
    {
        _reclassified = null;
        _session?.Dispose();
        _session = null;
    }
}

/// <summary>
/// Writes one message of the transcript: its lines (wrapped to the columns), their spans and actions, its code blocks.
/// Markdown is rendered here (headings, lists, quotes, tables, rules, links, inline code, bold, italic, fenced code);
/// nothing here is on a frame's path (a message is written when it changes).
/// </summary>
internal sealed class TranscriptWriter
{
    private static readonly SearchValues<char> InlineMarks = SearchValues.Create("`*_[\\h!");

    public StringBuilder Text { get; } = new();

    public List<TranscriptLine> Lines { get; } = [];

    public List<LineSpan> Spans { get; } = [];

    public List<LineAction> Actions { get; } = [];

    public List<ChatCodeBlock> Blocks { get; } = [];

    public int Columns { get; set; } = 80;

    public int FoldLines { get; set; } = 30;

    /// <summary>The source line of a code block's first line while <see cref="Markdown"/> writes one, else -1.</summary>
    public int CodeSourceStart { get; set; } = -1;

    /// <summary>The Markdown source's line being written (each line written takes it), or -1.</summary>
    public int SourceLine { get; set; } = -1;

    /// <summary>
    /// Whether it writes a document's preview (X part 9) rather than a chat message: a code block offers Copy only (no
    /// Insert, no Run), and an image is a link to its file.
    /// </summary>
    public bool Preview { get; set; }

    public void Clear()
    {
        Text.Clear();
        Lines.Clear();
        Spans.Clear();
        Actions.Clear();
        Blocks.Clear();
    }

    /// <summary>A line of <paramref name="content"/> after <paramref name="indent"/> spaces (all of it in <paramref name="cls"/> when given); its index.</summary>
    public int Line(TranscriptLineKind kind, ReadOnlySpan<char> content, int indent = 0, string? cls = null)
    {
        var line = new TranscriptLine
        {
            Kind = kind, Start = Text.Length, Indent = indent, SpanStart = Spans.Count, ActionStart = Actions.Count, Block = -1, SourceLine = SourceLine,
        };
        Text.Append(' ', indent).Append(content);
        line.Length = Text.Length - line.Start;
        Text.Append('\n');
        Lines.Add(line);
        if (cls is not null && content.Length > 0)
        {
            Span(indent, content.Length, cls);
        }

        return Lines.Count - 1;
    }

    /// <summary>A span on the last line, by column.</summary>
    public void Span(int column, int length, string cls)
    {
        if (length <= 0)
        {
            return;
        }

        Spans.Add(new LineSpan(column, length, cls));
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Lines);
        span[^1].SpanCount++;
    }

    /// <summary>An action on the last line, by column (its label drawn in <paramref name="cls"/>).</summary>
    public void Action(int column, int length, TranscriptAction action, string cls = ChatClasses.Action)
    {
        Actions.Add(new LineAction(column, length, action));
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Lines)[^1].ActionCount++;
        Span(column, length, cls);
    }

    /// <summary>Marks the last line's fill (a bubble's, a code block's) from <paramref name="left"/>, <paramref name="width"/> columns wide.</summary>
    public void Box(int left, int width)
    {
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Lines);
        span[^1].BoxLeft = left;
        span[^1].BoxWidth = width;
    }

    /// <summary>
    /// A line with <paramref name="left"/> at the start and the actions at the right edge (each label separated by two
    /// spaces); the actions' class <paramref name="actionClass"/>, a primary one in the accent.
    /// </summary>
    public void LabelAndActions(TranscriptLineKind kind, string left, string? leftClass, IReadOnlyList<(string Label, TranscriptAction Action, bool Primary)> actions,
        string actionClass = ChatClasses.Action, int indent = 0, bool alignLeft = false)
    {
        int width = 0;
        foreach (var (label, _, _) in actions)
        {
            width += label.Length + 2;
        }

        int room = Columns - indent - left.Length;
        int gap = alignLeft ? 3 : Math.Max(3, room - width + 2);
        var text = new StringBuilder(left);
        if (actions.Count > 0)
        {
            text.Append(' ', gap);
        }

        var columns = new int[actions.Count];
        for (int i = 0; i < actions.Count; i++)
        {
            columns[i] = indent + text.Length;
            text.Append(actions[i].Label);
            if (i < actions.Count - 1)
            {
                text.Append("  ");
            }
        }

        Line(kind, text.ToString(), indent);
        if (leftClass is not null)
        {
            Span(indent, left.Length, leftClass);
        }

        for (int i = 0; i < actions.Count; i++)
        {
            Action(columns[i], actions[i].Label.Length, actions[i].Action, actions[i].Primary ? ChatClasses.Primary : actionClass);
        }
    }

    /// <summary>
    /// <paramref name="text"/> wrapped to the columns, the first line after <paramref name="firstPrefix"/> and the rest after
    /// as many spaces, each line's spans clipped from <paramref name="spans"/> (by index into the text); links as actions.
    /// </summary>
    public void Wrapped(TranscriptLineKind kind, string text, List<LineSpan>? spans, int indent, string firstPrefix = "", string? baseClass = null,
        int entry = -1, List<(int Start, int Length, string Url)>? links = null, int width = 0)
    {
        int prefix = firstPrefix.Length;
        int room = Math.Max(8, (width > 0 ? width : Columns) - indent - prefix);
        int at = 0;
        bool first = true;
        if (text.Length == 0)
        {
            Line(kind, firstPrefix, indent);
            return;
        }

        while (at < text.Length)
        {
            int end = Math.Min(text.Length, at + room);
            if (end < text.Length)
            {
                int space = text.LastIndexOf(' ', end, end - at);
                if (space > at)
                {
                    end = space;
                }
            }

            string head = first ? firstPrefix : new string(' ', prefix);
            Line(kind, string.Concat(head, text.AsSpan(at, end - at)), indent);
            if (!first)
            {
                var wrapped = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Lines);
                wrapped[^1].Continuation = true;
                wrapped[^1].Indent = indent + prefix;
            }

            if (first && prefix > 0 && baseClass is null)
            {
                Span(indent, prefix, ChatClasses.Quiet);
            }

            if (baseClass is not null)
            {
                Span(indent + prefix, end - at, baseClass);
            }

            int column = indent + prefix - at;
            if (spans is not null)
            {
                foreach (var span in spans)
                {
                    int s = Math.Max(span.Column, at);
                    int e = Math.Min(span.Column + span.Length, end);
                    if (e > s)
                    {
                        Span(column + s, e - s, span.Class);
                    }
                }
            }

            if (links is not null)
            {
                foreach (var (start, length, url) in links)
                {
                    int s = Math.Max(start, at);
                    int e = Math.Min(start + length, end);
                    if (e > s)
                    {
                        Actions.Add(new LineAction(column + s, e - s, new TranscriptAction(TranscriptActionKind.Link, entry) { Text = url }));
                        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Lines)[^1].ActionCount++;
                    }
                }
            }

            first = false;
            at = end;
            while (at < text.Length && text[at] == ' ')
            {
                at++;
            }
        }
    }

    /// <summary>
    /// A code block: its header (language, Copy, Insert at caret, the entry's own actions, Run in terminal for a shell
    /// block), its lines in their fill (wrapped at the columns), folded past <see cref="FoldLines"/> unless shown whole.
    /// </summary>
    public void Code(int entry, int ordinal, string code, string? language, IReadOnlyList<PanelAction> entryActions, bool colour, bool shownAll)
    {
        string shown = code.Replace("\t", "    ", StringComparison.Ordinal).TrimEnd('\n', '\r').Replace("\r", "", StringComparison.Ordinal);
        var block = new ChatCodeBlock(language ?? "", shown, colour);
        int blockIndex = Blocks.Count;
        Blocks.Add(block);
        long key = ((long)entry << 16) | (uint)ordinal;
        var actions = new List<(string, TranscriptAction, bool)>
        {
            ("Copy", new TranscriptAction(TranscriptActionKind.CopyCode, entry) { Text = shown }, false),
        };
        if (!Preview)
        {
            actions.Add(("Insert at caret", new TranscriptAction(TranscriptActionKind.InsertCode, entry) { Text = shown }, false));
        }

        foreach (var action in entryActions)
        {
            actions.Add((action.Label, new TranscriptAction(TranscriptActionKind.Panel, entry) { Action = action }, action.Primary));
        }

        if (block.IsShell && !Preview)
        {
            actions.Add(("Run in terminal", new TranscriptAction(TranscriptActionKind.RunCode, entry) { Text = shown }, false));
        }

        LabelAndActions(TranscriptLineKind.CodeHeader, language is { Length: > 0 } named ? named : "code", ChatClasses.Quiet, actions, indent: 1);
        Box(0, Columns);
        int width = Math.Max(8, Columns - 3);
        int lineCount = 0;
        int folded = FoldLines > 0 && !shownAll ? FoldLines : int.MaxValue;
        int total = 1;
        foreach (char c in shown)
        {
            total += c == '\n' ? 1 : 0;
        }

        int at = 0;
        while (at <= shown.Length && lineCount < folded)
        {
            int end = shown.IndexOf('\n', at);
            if (end < 0)
            {
                end = shown.Length;
            }

            int start = at;
            if (CodeSourceStart >= 0)
            {
                SourceLine = CodeSourceStart + lineCount;
            }

            do
            {
                int piece = Math.Min(width, end - start);
                Line(TranscriptLineKind.Code, shown.AsSpan(start, piece), 2);
                var lines = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Lines);
                lines[^1].Block = blockIndex;
                lines[^1].BlockOffset = start;
                Box(0, Columns);
                start += piece;
            }
            while (start < end);

            lineCount++;
            at = end + 1;
        }

        if (total > FoldLines && FoldLines > 0)
        {
            string label = shownAll ? "Fold" : "Show all";
            string left = shownAll ? $"{total} lines" : $"⋯ {total - lineCount} more lines";
            LabelAndActions(TranscriptLineKind.Fold, left, ChatClasses.Quiet, [(label, new TranscriptAction(TranscriptActionKind.ShowAll, entry) { Key = key }, false)], indent: 2, alignLeft: true);
            Box(0, Columns);
        }
    }

    /// <summary>
    /// <paramref name="markdown"/> rendered: headings, lists, quotes, tables, rules, paragraphs (each source line its own),
    /// fenced code (a block left open while a reply streams runs to the end).
    /// </summary>
    public void Markdown(int entry, string markdown, bool colour, Func<long, bool> shownAll)
    {
        var lines = markdown.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        int ordinal = 0;
        bool blank = true;
        bool tracking = SourceLine >= 0;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.TrimStart();
            if (tracking)
            {
                SourceLine = i;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                string fence = trimmed[..3];
                string language = trimmed[3..].Trim();
                var code = new StringBuilder();
                int j = i + 1;
                for (; j < lines.Length && !lines[j].TrimStart().StartsWith(fence, StringComparison.Ordinal); j++)
                {
                    code.Append(lines[j]).Append('\n');
                }

                long key = ((long)entry << 16) | (uint)ordinal;
                CodeSourceStart = tracking ? i + 1 : -1;
                Code(entry, ordinal++, code.ToString(), language, [], colour, shownAll(key));
                CodeSourceStart = -1;
                i = j;
                blank = false;
                continue;
            }

            if (trimmed.Length == 0)
            {
                if (!blank)
                {
                    Line(TranscriptLineKind.Body, "");
                }

                blank = true;
                continue;
            }

            blank = false;
            if (trimmed[0] == '|' && i + 1 < lines.Length && IsTableRule(lines[i + 1]))
            {
                var rows = new List<string[]>();
                int j = i;
                for (; j < lines.Length && lines[j].TrimStart().StartsWith('|'); j++)
                {
                    if (j != i + 1)
                    {
                        rows.Add(Cells(lines[j]));
                    }
                }

                Table(entry, rows, tracking ? i : -1);
                i = j - 1;
                continue;
            }

            int hashes = 0;
            while (hashes < trimmed.Length && hashes < 6 && trimmed[hashes] == '#')
            {
                hashes++;
            }

            if (hashes > 0 && hashes < trimmed.Length && trimmed[hashes] == ' ')
            {
                Inline(entry, TranscriptLineKind.Body, trimmed[(hashes + 1)..].Trim(), 0, "", ChatClasses.Heading);
                continue;
            }

            if (IsRule(trimmed))
            {
                Line(TranscriptLineKind.Body, new string('─', Math.Max(4, Columns - 2)), 1, ChatClasses.Quiet);
                continue;
            }

            if (trimmed[0] == '>')
            {
                Inline(entry, TranscriptLineKind.Body, trimmed[1..].TrimStart(), 0, "│ ", ChatClasses.Quiet);
                continue;
            }

            int depth = Math.Min(6, (line.Length - trimmed.Length) / 2);
            if (ListMarker(trimmed) is { } marker)
            {
                string bullet = marker.Ordered ? trimmed[..marker.Length] + " " : "• ";
                Inline(entry, TranscriptLineKind.Body, trimmed[(marker.Length + 1)..].TrimStart(), depth * 2, bullet, null);
                continue;
            }

            Inline(entry, TranscriptLineKind.Body, trimmed, depth * 2, "", null);
        }
    }

    private static bool IsRule(string trimmed)
    {
        if (trimmed.Length < 3)
        {
            return false;
        }

        char c = trimmed[0];
        if (c is not ('-' or '*' or '_'))
        {
            return false;
        }

        foreach (char other in trimmed)
        {
            if (other != c && other != ' ')
            {
                return false;
            }
        }

        return true;
    }

    private static (bool Ordered, int Length)? ListMarker(string trimmed)
    {
        if (trimmed.Length >= 2 && trimmed[0] is '-' or '*' or '+' && trimmed[1] == ' ')
        {
            return (false, 1);
        }

        int digits = 0;
        while (digits < trimmed.Length && digits < 4 && char.IsAsciiDigit(trimmed[digits]))
        {
            digits++;
        }

        return digits > 0 && digits + 1 < trimmed.Length && trimmed[digits] is '.' or ')' && trimmed[digits + 1] == ' ' ? (true, digits + 1) : null;
    }

    private static bool IsTableRule(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length < 3 || trimmed[0] != '|')
        {
            return false;
        }

        foreach (char c in trimmed)
        {
            if (c is not ('|' or '-' or ':' or ' '))
            {
                return false;
            }
        }

        return trimmed.Contains('-', StringComparison.Ordinal);
    }

    private static string[] Cells(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.EndsWith('|'))
        {
            trimmed = trimmed[..^1];
        }

        return [.. trimmed.Split('|').Select(c => c.Trim())];
    }

    // A table: the columns as wide as their widest cell (inline marks taken out), the header bold, a rule under it.
    private void Table(int entry, List<string[]> rows, int firstSource = -1)
    {
        int count = rows.Max(r => r.Length);
        var shown = rows.Select(r => r.Select(c => Plain(c)).ToArray()).ToList();
        var widths = new int[count];
        foreach (var row in shown)
        {
            for (int c = 0; c < row.Length; c++)
            {
                widths[c] = Math.Max(widths[c], row[c].Length);
            }
        }

        for (int r = 0; r < shown.Count; r++)
        {
            if (firstSource >= 0)
            {
                SourceLine = firstSource + (r == 0 ? 0 : r + 1);   // the rule under the header is the source's second line
            }

            var text = new StringBuilder();
            var columns = new List<(int Column, int Length)>();
            for (int c = 0; c < count; c++)
            {
                string cell = c < shown[r].Length ? shown[r][c] : "";
                if (c > 0)
                {
                    text.Append(" │ ");
                }

                columns.Add((text.Length, cell.Length));
                text.Append(cell.PadRight(widths[c]));
            }

            Line(TranscriptLineKind.Body, text.ToString().TrimEnd(), 1);
            for (int c = 0; c < count; c++)
            {
                if (c > 0)
                {
                    Span(1 + columns[c].Column - 2, 1, ChatClasses.Quiet);
                }

                if (r == 0)
                {
                    Span(1 + columns[c].Column, columns[c].Length, ChatClasses.Bold);
                }
            }

            if (r == 0)
            {
                Line(TranscriptLineKind.Body, string.Join("─┼─", widths.Select(w => new string('─', w))), 1, ChatClasses.Quiet);
            }
        }

        _ = entry;
    }

    /// <summary><paramref name="markdown"/>'s inline marks taken out (code, bold, italic, links): the text as shown.</summary>
    internal static string Plain(string markdown) => ParseInline(markdown, null, null);

    // One line's inline Markdown, wrapped, with its spans and its links.
    private void Inline(int entry, TranscriptLineKind kind, string source, int indent, string prefix, string? baseClass)
    {
        var spans = new List<LineSpan>();
        var links = new List<(int, int, string)>();
        string text = ParseInline(source, spans, links);
        if (baseClass is not null)
        {
            spans.Insert(0, new LineSpan(0, text.Length, baseClass));
        }

        Wrapped(kind, text, spans, indent, prefix, null, entry, links);
    }

    // The text as shown, the spans (by index into it) and the links (start, length, address).
    private static string ParseInline(string source, List<LineSpan>? spans, List<(int, int, string)>? links)
    {
        if (source.AsSpan().IndexOfAny(InlineMarks) < 0)
        {
            return source;
        }

        var text = new StringBuilder(source.Length);
        int i = 0;
        while (i < source.Length)
        {
            char c = source[i];
            if (c == '\\' && i + 1 < source.Length && !char.IsLetterOrDigit(source[i + 1]))
            {
                text.Append(source[i + 1]);
                i += 2;
                continue;
            }

            if (c == '`')
            {
                int close = source.IndexOf('`', i + 1);
                if (close > i)
                {
                    spans?.Add(new LineSpan(text.Length, close - i - 1, ChatClasses.InlineCode));
                    text.Append(source, i + 1, close - i - 1);
                    i = close + 1;
                    continue;
                }
            }

            if ((c == '*' || c == '_') && i + 1 < source.Length && source[i + 1] == c)
            {
                int close = source.IndexOf(new string(c, 2), i + 2, StringComparison.Ordinal);
                if (close > i + 2)
                {
                    spans?.Add(new LineSpan(text.Length, close - i - 2, ChatClasses.Bold));
                    text.Append(source, i + 2, close - i - 2);
                    i = close + 2;
                    continue;
                }
            }

            if ((c == '*' || (c == '_' && (i == 0 || !char.IsLetterOrDigit(source[i - 1])))) && i + 1 < source.Length && source[i + 1] != ' ')
            {
                int close = source.IndexOf(c, i + 1);
                if (close > i + 1 && source[close - 1] != ' ' && (c != '_' || close + 1 >= source.Length || !char.IsLetterOrDigit(source[close + 1])))
                {
                    spans?.Add(new LineSpan(text.Length, close - i - 1, ChatClasses.Italic));
                    text.Append(source, i + 1, close - i - 1);
                    i = close + 1;
                    continue;
                }
            }

            if (c == '!' && i + 1 < source.Length && source[i + 1] == '[')
            {
                int middle = source.IndexOf("](", i + 2, StringComparison.Ordinal);
                int close = middle > 0 ? source.IndexOf(')', middle + 2) : -1;
                if (middle > i && close > middle)
                {
                    // An image: a link to its file (X part 9's preview opens it in the image preview).
                    string label = "🖼 " + (middle > i + 2 ? source[(i + 2)..middle] : "image");
                    string url = source[(middle + 2)..close].Trim();
                    int space = url.IndexOf(' ', StringComparison.Ordinal);
                    url = space > 0 ? url[..space] : url;             // ![a](b.png "title"): the title left out
                    spans?.Add(new LineSpan(text.Length, label.Length, ChatClasses.Link));
                    links?.Add((text.Length, label.Length, url));
                    text.Append(label);
                    i = close + 1;
                    continue;
                }
            }

            if (c == '[')
            {
                int middle = source.IndexOf("](", i + 1, StringComparison.Ordinal);
                int close = middle > 0 ? source.IndexOf(')', middle + 2) : -1;
                if (middle > i && close > middle)
                {
                    string label = source[(i + 1)..middle];
                    string url = source[(middle + 2)..close];
                    spans?.Add(new LineSpan(text.Length, label.Length, ChatClasses.Link));
                    links?.Add((text.Length, label.Length, url));
                    text.Append(label);
                    i = close + 1;
                    continue;
                }
            }

            if (c == 'h' && (i == 0 || source[i - 1] == ' ' || source[i - 1] == '(')
                && (source.AsSpan(i).StartsWith("https://", StringComparison.Ordinal) || source.AsSpan(i).StartsWith("http://", StringComparison.Ordinal)))
            {
                int end = source.IndexOfAny([' ', ')', '>'], i);
                if (end < 0)
                {
                    end = source.Length;
                }

                string url = source[i..end].TrimEnd('.', ',', ';', ':');
                spans?.Add(new LineSpan(text.Length, url.Length, ChatClasses.Link));
                links?.Add((text.Length, url.Length, url));
                text.Append(url);
                i += url.Length;
                continue;
            }

            text.Append(c);
            i++;
        }

        return text.ToString();
    }
}
