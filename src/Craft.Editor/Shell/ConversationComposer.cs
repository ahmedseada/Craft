// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;

namespace Craft.Editor.Shell;

/// <summary>What a suggestion under the composer is.</summary>
public enum ComposerSuggestionKind
{
    /// <summary>A slash command ("/explain").</summary>
    Command,

    /// <summary>A file of the workspace ("@src/Program.cs").</summary>
    File,

    /// <summary>A symbol of an open document ("@HouseBuilder").</summary>
    Symbol,
}

/// <summary>A row of the composer's picker: what it shows, what it puts in the message, and a quiet detail.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Label">What it shows ("/explain", "Program.cs").</param>
/// <param name="Insert">What replaces the typed word ("/explain ", "@src/Program.cs ").</param>
/// <param name="Detail">A quiet second text (what a command does, a file's folder), or null.</param>
public sealed record ComposerSuggestion(ComposerSuggestionKind Kind, string Label, string Insert, string? Detail = null);

/// <summary>
/// The chat's and the agent's message box, headless (X part 10): its text and caret; it grows up to
/// <c>CRAFT_COMPOSER_MAX_LINES</c> lines, then scrolls; the sent messages come back with Up and Down while it is empty (or
/// while going through them); "/" lists the panel's slash commands as you type, "@" the workspace's files and the open
/// documents' symbols (a picker: Up and Down move, Tab or Enter takes one, Escape closes it); "Edit and resend" puts a
/// message back in it, and Send then replaces that message. The view mirrors <see cref="Text"/> and draws
/// <see cref="Suggestions"/>; every key goes to <see cref="HandleKey"/> first. Used on the UI thread; the files are listed
/// off it.
/// </summary>
public sealed class ConversationComposer
{
    private readonly List<string> _history = [];
    private IReadOnlyList<ComposerSuggestion> _suggestions = [];
    private int _historyAt = -1;
    private string _draft = "";
    private int _selected;
    private IReadOnlyList<string>? _files;
    private string? _filesRoot;

    /// <summary>The composer's settings (its height, the history kept, the picker).</summary>
    public ComposerOptions Settings { get; set; } = new();

    /// <summary>The panel the composer sends to, or null.</summary>
    public IConversationPanel? Panel { get; set; }

    /// <summary>The message as typed.</summary>
    public string Text { get; private set; } = "";

    /// <summary>The caret's place in <see cref="Text"/>.</summary>
    public int Caret { get; private set; }

    /// <summary>The entry being edited for "Edit and resend", or -1.</summary>
    public int EditingEntry { get; private set; } = -1;

    /// <summary>The picker's rows (slash commands or mentions), empty when closed.</summary>
    public IReadOnlyList<ComposerSuggestion> Suggestions => _suggestions;

    /// <summary>The picker's chosen row.</summary>
    public int Selected => _selected;

    /// <summary>The sent messages, oldest first.</summary>
    public IReadOnlyList<string> History => _history;

    /// <summary>The lines the box shows: its text's lines, at least one, at most <see cref="ComposerOptions.MaxLines"/>.</summary>
    public int VisibleLines => Math.Clamp(LineCount, 1, Math.Max(1, Settings.MaxLines));

    /// <summary>Whether the text is longer than the box grows (it scrolls).</summary>
    public bool Scrolls => LineCount > Math.Max(1, Settings.MaxLines);

    /// <summary>The text's lines.</summary>
    public int LineCount
    {
        get
        {
            int lines = 1;
            foreach (char c in Text)
            {
                lines += c == '\n' ? 1 : 0;
            }

            return lines;
        }
    }

    /// <summary>The workspace's folder, whose files "@" lists, or null.</summary>
    public Func<string?>? WorkspaceFolder { get; set; }

    /// <summary>The open documents' symbols "@" lists (names and where), or null.</summary>
    public Func<IReadOnlyList<ComposerSuggestion>>? Symbols { get; set; }

    /// <summary>Runs work off the UI thread and brings its end back (the tests' serial host, the app's dispatcher).</summary>
    public Action<Action> Post { get; set; } = static action => action();

    /// <summary>Raised when the text or the caret changed from here (the view puts them in its box).</summary>
    public event EventHandler? TextChanged;

    /// <summary>Raised when the picker's rows or its chosen row changed.</summary>
    public event EventHandler? SuggestionsChanged;

    /// <summary>The picker's last listing of files (the tests await it).</summary>
    public Task Listing { get; private set; } = Task.CompletedTask;

    /// <summary>The box's text changed by the user (typed, pasted): the picker follows it.</summary>
    public void Typed(string text, int caret)
    {
        Text = text ?? "";
        Caret = Math.Clamp(caret, 0, Text.Length);
        if (_historyAt >= 0 && !string.Equals(Text, _history[_historyAt], StringComparison.Ordinal))
        {
            _historyAt = -1;                 // typed into a recalled message: it is a new draft now
        }

        Suggest();
    }

    /// <summary>Puts <paramref name="text"/> in the box, the caret at its end (a quote, a message to edit).</summary>
    public void SetText(string text)
    {
        Text = text ?? "";
        Caret = Text.Length;
        _historyAt = -1;
        Close();
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds <paramref name="quote"/> at the start of the message (Quote in reply).</summary>
    public void Quote(string quote)
    {
        if (!string.IsNullOrEmpty(quote))
        {
            SetText(quote + Text);
        }
    }

    /// <summary>Puts the user's message <paramref name="entry"/> (<paramref name="text"/>) back to edit: Send replaces it.</summary>
    public void Edit(int entry, string text)
    {
        EditingEntry = entry;
        SetText(text);
    }

    /// <summary>Leaves "Edit and resend" (the text stays).</summary>
    public void CancelEdit() => EditingEntry = -1;

    /// <summary>
    /// A key in the box, before the box and the composer's bindings: the picker's keys while it is open, the history's
    /// Up and Down while the box is empty (or showing a recalled message). True when it was taken.
    /// </summary>
    public bool HandleKey(EditorKey key, KeyModifiers modifiers)
    {
        if (_suggestions.Count > 0 && modifiers == KeyModifiers.None)
        {
            switch (key)
            {
                case EditorKey.Up:
                    Choose(_selected <= 0 ? _suggestions.Count - 1 : _selected - 1);
                    return true;
                case EditorKey.Down:
                    Choose(_selected >= _suggestions.Count - 1 ? 0 : _selected + 1);
                    return true;
                case EditorKey.Tab:
                case EditorKey.Enter:
                    Accept(_selected);
                    return true;
                case EditorKey.Escape:
                    Close();
                    return true;
            }
        }

        if (modifiers != KeyModifiers.None || (Text.Length > 0 && _historyAt < 0))
        {
            return false;
        }

        if (key == EditorKey.Up && _history.Count > 0)
        {
            if (_historyAt < 0)
            {
                _draft = Text;
                _historyAt = _history.Count;
            }

            _historyAt = Math.Max(0, _historyAt - 1);
            Recall(_history[_historyAt]);
            return true;
        }

        if (key == EditorKey.Down && _historyAt >= 0)
        {
            _historyAt++;
            if (_historyAt >= _history.Count)
            {
                _historyAt = -1;
                Recall(_draft);
            }
            else
            {
                Recall(_history[_historyAt]);
            }

            return true;
        }

        return false;
    }

    /// <summary>Takes the picker's row <paramref name="index"/>: it replaces the typed "/" or "@" word.</summary>
    public void Accept(int index)
    {
        if ((uint)index >= (uint)_suggestions.Count)
        {
            return;
        }

        var row = _suggestions[index];
        var (start, end) = WordAtCaret();
        Text = string.Concat(Text.AsSpan(0, start), row.Insert, Text.AsSpan(end));
        Caret = start + row.Insert.Length;
        Close();
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Closes the picker.</summary>
    public void Close()
    {
        if (_suggestions.Count > 0)
        {
            _suggestions = [];
            _selected = 0;
            SuggestionsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Sends the message (or, editing, replaces the message being edited and sends it again): kept in the history, the box
    /// emptied. Completes when the panel took it.
    /// </summary>
    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        string text = Text.Trim();
        if (text.Length == 0 || Panel is not { } panel)
        {
            return;
        }

        int editing = EditingEntry;
        EditingEntry = -1;
        if (_history.Count == 0 || !string.Equals(_history[^1], text, StringComparison.Ordinal))
        {
            _history.Add(text);
            if (_history.Count > Math.Max(1, Settings.HistoryLimit))
            {
                _history.RemoveAt(0);
            }
        }

        _historyAt = -1;
        _draft = "";
        SetText("");
        if (editing >= 0)
        {
            await panel.EditAndResendAsync(editing, text, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await panel.SubmitAsync(text, cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>Stops the panel's work (its header's Stop), if it is busy.</summary>
    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Panel is { IsBusy: true } panel && panel.HeaderActions.FirstOrDefault(a => a.Label == "Stop") is { } stop ? stop.RunAsync(cancellationToken) : Task.CompletedTask;

    private void Recall(string text)
    {
        Text = text;
        Caret = text.Length;
        Close();
        TextChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Choose(int index)
    {
        _selected = index;
        SuggestionsChanged?.Invoke(this, EventArgs.Empty);
    }

    // The "/" or "@" word the caret is in: its start and end.
    private (int Start, int End) WordAtCaret()
    {
        int start = Caret;
        while (start > 0 && !char.IsWhiteSpace(Text[start - 1]))
        {
            start--;
        }

        int end = Caret;
        while (end < Text.Length && !char.IsWhiteSpace(Text[end]))
        {
            end++;
        }

        return (start, end);
    }

    // The picker for what is typed: "/name" at the message's start lists the slash commands; "@word" files and symbols.
    private void Suggest()
    {
        var (start, end) = WordAtCaret();
        string word = Text[start..Caret];
        _ = end;
        if (start == 0 && word.StartsWith('/') && Panel is { } panel)
        {
            string typed = word[1..];
            var rows = panel.SlashCommands
                .Where(c => c.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                .Select(c => new ComposerSuggestion(ComposerSuggestionKind.Command, "/" + c.Name, "/" + c.Name + " ", c.Description))
                .ToList();
            Show(rows);
            return;
        }

        if (word.StartsWith('@'))
        {
            Mention(word[1..]);
            return;
        }

        Close();
    }

    private void Show(IReadOnlyList<ComposerSuggestion> rows)
    {
        _suggestions = rows;
        _selected = 0;
        SuggestionsChanged?.Invoke(this, EventArgs.Empty);
    }

    // "@query": the open documents' symbols at once, the workspace's files when listed (off the UI thread, once per folder).
    private void Mention(string query)
    {
        string? root = WorkspaceFolder?.Invoke();
        if (root is not null && (!string.Equals(root, _filesRoot, StringComparison.Ordinal) || _files is null))
        {
            _filesRoot = root;
            _files = null;
            Listing = ListAsync(root);
        }

        var rows = new List<(int Score, ComposerSuggestion Row)>();
        foreach (var symbol in Symbols?.Invoke() ?? [])
        {
            if (Score(symbol.Label, query) is int score)
            {
                rows.Add((score, symbol));
            }
        }

        foreach (var file in _files ?? [])
        {
            string name = Path.GetFileName(file);
            if ((Score(name, query) ?? Score(file, query) + 100) is int score)
            {
                string folder = Path.GetDirectoryName(file)?.Replace('\\', '/') ?? "";
                rows.Add((score, new ComposerSuggestion(ComposerSuggestionKind.File, name, "@" + file + " ", folder.Length > 0 ? folder : null)));
            }
        }

        Show([.. rows.OrderBy(r => r.Score).ThenBy(r => r.Row.Label, StringComparer.OrdinalIgnoreCase).Take(Math.Max(1, Settings.MentionRows)).Select(r => r.Row)]);
    }

    // The workspace's files listed on the thread pool, then taken on the UI thread (the rows follow while "@" is typed).
    private async Task ListAsync(string root)
    {
        IReadOnlyList<string> files;
        try
        {
            var ignored = Settings.IgnoredFolders;
            int limit = Settings.MentionFileLimit;
            files = await Task.Run(() => ListFiles(root, ignored, limit)).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var taken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            if (string.Equals(root, _filesRoot, StringComparison.Ordinal))
            {
                _files = files;
                var (start, _) = WordAtCaret();
                if (Caret > start && Text[start] == '@')
                {
                    Suggest();                // still typing a mention: the files join the rows
                }
            }

            taken.TrySetResult();
        });
        await taken.Task.ConfigureAwait(false);
    }

    // How well `text` matches `query`: 0 a prefix, 1 a substring, 2 the letters in order; null none. Ignoring case.
    private static int? Score(string text, string query)
    {
        if (query.Length == 0)
        {
            return 1;
        }

        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        int at = 0;
        foreach (char c in text)
        {
            if (at < query.Length && char.ToLowerInvariant(c) == char.ToLowerInvariant(query[at]))
            {
                at++;
            }
        }

        return at == query.Length ? 2 : null;
    }

    /// <summary>The files under <paramref name="root"/> (paths relative to it, '/' between folders), the ignored folders skipped, at most <paramref name="limit"/>.</summary>
    public static IReadOnlyList<string> ListFiles(string root, IReadOnlyList<string> ignored, int limit)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(ignored);
        var files = new List<string>();
        var folders = new Stack<string>();
        folders.Push(root);
        var skip = new HashSet<string>(ignored, StringComparer.OrdinalIgnoreCase);
        while (folders.Count > 0 && files.Count < limit)
        {
            string folder = folders.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    files.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
                    if (files.Count >= limit)
                    {
                        break;
                    }
                }

                foreach (var sub in Directory.EnumerateDirectories(folder))
                {
                    if (!skip.Contains(Path.GetFileName(sub)))
                    {
                        folders.Push(sub);
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be read lists nothing.
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    /// <summary>
    /// The symbols of <paramref name="model"/>'s text as "@" rows (its language's document symbols: types, members, enum
    /// members, each name once, in the order of the text, at most <paramref name="limit"/>): the last version the language
    /// answered, read without waiting. When that is older than the text, the current version is asked off the UI thread and
    /// <paramref name="landed"/> runs on the UI thread once it is in (the picker lists again).
    /// </summary>
    public static IReadOnlyList<ComposerSuggestion> SymbolsOf(EditorModel? model, Action? landed = null, int limit = 4000)
    {
        if (model?.LanguageSession is null)
        {
            return [];
        }

        var index = model.Symbols;
        if (index is null || index.Version != model.Snapshot.Version)
        {
            model.RequestSymbols(landed ?? (static () => { }));
        }

        if (index is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<ComposerSuggestion>();
        string file = model.Document.FilePath is { } path ? Path.GetFileName(path) : "untitled";
        for (int i = 0; i < index.Count && rows.Count < limit; i++)
        {
            var symbol = index.Symbols[i];
            if (symbol.Kind != ScopeKind.Namespace && seen.Add(symbol.Name))
            {
                rows.Add(new ComposerSuggestion(ComposerSuggestionKind.Symbol, symbol.Name, "@" + symbol.Name + " ", $"{SymbolKinds.Word(symbol.Kind)} in {file}"));
            }
        }

        return rows;
    }

    /// <summary>Lists the picker again for the "@" word at the caret (the symbols it lists landed); nothing when no "@" word is typed.</summary>
    public void RefreshSuggestions()
    {
        var (start, _) = WordAtCaret();
        if (start < Caret && Text[start] == '@')
        {
            Suggest();
        }
    }
}

/// <summary>The composer's settings: <c>CRAFT_COMPOSER_MAX_LINES</c>, the history kept, the picker's rows and files.</summary>
public sealed record ComposerOptions
{
    /// <summary>The lines the box grows to (<c>CRAFT_COMPOSER_MAX_LINES</c>).</summary>
    public int MaxLines { get; init; } = 12;

    /// <summary>The sent messages kept for Up and Down.</summary>
    public int HistoryLimit { get; init; } = 100;

    /// <summary>The picker's rows.</summary>
    public int MentionRows { get; init; } = 12;

    /// <summary>The most files "@" lists from the workspace.</summary>
    public int MentionFileLimit { get; init; } = 20_000;

    /// <summary>The folders "@" never lists (the workspace's ignored folders).</summary>
    public IReadOnlyList<string> IgnoredFolders { get; init; } = [".git", "bin", "obj", "node_modules", ".vs", ".craft"];

    /// <summary>The composer's settings from <paramref name="options"/>.</summary>
    public static ComposerOptions From(CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new ComposerOptions { MaxLines = options.ComposerMaxLines, IgnoredFolders = [.. options.WorkspaceIgnore, ".craft"] };
    }
}
