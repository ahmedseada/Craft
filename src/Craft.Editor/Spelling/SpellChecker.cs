// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Frozen;
using System.Text;
using Craft.Abstractions;
using Craft.Core;

namespace Craft.Editor.Spelling;

/// <summary>
/// The spell check's words (X part 9, item 22): the dictionary of <c>CRAFT_SPELL_LANGUAGE</c> (<see cref="SpellDictionaries"/>;
/// the bundled "en" registered on first use), the user's own words (<c>spelling/words.txt</c> in the data folder: Add to
/// dictionary) and the workspace's (<c>.craft/words.txt</c>: Ignore in workspace), each a set ignoring case looked up by
/// span. Its <see cref="Version"/> changes whenever what it knows changes (a dictionary landed, a word added), and the
/// editors it serves are drawn again then. The files are read and written off the UI thread; a word added is known at once.
/// </summary>
public sealed class SpellChecker
{
    /// <summary>The user's words' file, under the data folder.</summary>
    public const string UserFileName = "spelling/words.txt";

    /// <summary>The workspace's words' file, under its root.</summary>
    public const string WorkspaceFileName = ".craft/words.txt";

    private readonly List<WeakReference<EditorModel>> _editors = [];
    private Words _user = Words.Empty;
    private Words _workspace = Words.Empty;
    private string _language = "en";
    private ISpellDictionary? _dictionary;
    private int _version = 1;

    /// <summary>The checker the shell configures and every editor reads by default.</summary>
    public static SpellChecker Shared { get; } = new();

    /// <summary>Changes whenever what it knows changes (its dictionary landed, a word was added, the language or files changed).</summary>
    public int Version => _version;

    /// <summary>Words shorter than this are not checked (<c>CRAFT_SPELL_MIN_LENGTH</c>).</summary>
    public int MinLength { get; set; } = 4;

    /// <summary>The suggestions offered at most (<c>CRAFT_SPELL_SUGGESTIONS</c>).</summary>
    public int MaxSuggestions { get; set; } = 5;

    /// <summary>The user's words' file (a full path), or null.</summary>
    public string? UserFile { get; private set; }

    /// <summary>The workspace's words' file (a full path), or null.</summary>
    public string? WorkspaceFile { get; private set; }

    /// <summary>How the editors are drawn again from another thread (the host's dispatcher); none when null (the next frame sees the change).</summary>
    public Action<Action>? Post { get; set; }

    /// <summary>Why the dictionary cannot be used ("not registered"), or null.</summary>
    public string? Problem { get; private set; }

    /// <summary>The language's dictionary, or null when none is registered under it (<see cref="Problem"/> says so).</summary>
    public ISpellDictionary? Dictionary
    {
        get
        {
            if (_dictionary is null && Problem is null)
            {
                EnsureEnglish();
                _dictionary = SpellDictionaries.Find(_language);
                if (_dictionary is null)
                {
                    Problem = $"Spell dictionary '{_language}' is not registered (registered: {string.Join(", ", SpellDictionaries.Names)}): CRAFT_SPELL_LANGUAGE.";
                }
                else if (!_dictionary.IsReady)
                {
                    var waiting = _dictionary;
                    _ = waiting.Ready.ContinueWith(_ => Bump(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }

            return _dictionary;
        }
    }

    /// <summary>Whether words can be checked now (a dictionary is registered and its words are in).</summary>
    public bool IsReady => Dictionary is { IsReady: true };

    /// <summary>Registers the bundled "en" in <see cref="SpellDictionaries"/> when no "en" is registered.</summary>
    public static void EnsureEnglish()
    {
        if (SpellDictionaries.Find("en") is null)
        {
            try
            {
                SpellDictionaries.Register(WordListDictionary.English);
            }
            catch (ArgumentException)
            {
                // Registered meanwhile by another thread: that one answers.
            }
        }
    }

    /// <summary>
    /// Takes <paramref name="options"/> (the language, the shortest word, the suggestions), the user's file under
    /// <paramref name="dataFolder"/> and the workspace's under <paramref name="workspaceRoot"/> (read off the calling thread).
    /// </summary>
    public void Configure(CraftOptions options, string? dataFolder, string? workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(options);
        MinLength = options.SpellMinLength;
        MaxSuggestions = options.SpellSuggestions;
        if (!string.Equals(_language, options.SpellLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _language = options.SpellLanguage;
            _dictionary = null;
            Problem = null;
        }

        string? user = dataFolder is null ? null : Path.GetFullPath(Path.Combine(dataFolder, UserFileName));
        string? workspace = workspaceRoot is null ? null : Path.GetFullPath(Path.Combine(workspaceRoot, WorkspaceFileName));
        if (user != UserFile)
        {
            UserFile = user;
            _user = Words.Empty;
            _ = LoadAsync(user, words => _user = words);
        }

        if (workspace != WorkspaceFile)
        {
            WorkspaceFile = workspace;
            _workspace = Words.Empty;
            _ = LoadAsync(workspace, words => _workspace = words);
        }

        Bump();
    }

    /// <summary>
    /// Whether <paramref name="word"/> is known: the dictionary's, the user's or the workspace's (ignoring case); true while
    /// no dictionary is ready. Allocates nothing.
    /// </summary>
    public bool IsKnown(ReadOnlySpan<char> word)
    {
        if (Dictionary is not { IsReady: true } dictionary)
        {
            return true;
        }

        return dictionary.Contains(word) || _user.Lookup.Contains(word) || _workspace.Lookup.Contains(word);
    }

    /// <summary>The dictionary's suggestions for <paramref name="word"/> (at most <see cref="MaxSuggestions"/>).</summary>
    public IReadOnlyList<string> Suggest(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return Dictionary is { IsReady: true } dictionary ? dictionary.Suggest(word, MaxSuggestions) : [];
    }

    /// <summary>Adds <paramref name="word"/> to the user's words (known at once; the file written off the UI thread); its write's problem, or null.</summary>
    public Task<string?> AddToUserAsync(string word) => AddAsync(word, UserFile, () => _user, words => _user = words, "the user's data folder");

    /// <summary>Adds <paramref name="word"/> to the workspace's words (<c>.craft/words.txt</c>); its write's problem, or null.</summary>
    public Task<string?> IgnoreInWorkspaceAsync(string word) => AddAsync(word, WorkspaceFile, () => _workspace, words => _workspace = words, "a workspace");

    /// <summary>The user's words now.</summary>
    public IReadOnlyCollection<string> UserWords => _user.Set;

    /// <summary>The workspace's words now.</summary>
    public IReadOnlyCollection<string> WorkspaceWords => _workspace.Set;

    /// <summary>Draws <paramref name="editor"/> again whenever what the checker knows changes (held weakly).</summary>
    internal void Serve(EditorModel editor)
    {
        lock (_editors)
        {
            _editors.RemoveAll(w => !w.TryGetTarget(out var alive) || alive.IsDisposed);
            _editors.Add(new WeakReference<EditorModel>(editor));
        }
    }

    private async Task<string?> AddAsync(string word, string? file, Func<Words> current, Action<Words> set, string where)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        word = word.Trim();
        var words = current();
        if (words.Set.Contains(word))
        {
            return null;
        }

        set(words.With(word));
        Bump();
        if (file is null)
        {
            return $"There is no {where} to keep the word in: it is known until the editor closes.";
        }

        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.AppendAllText(file, word + "\n", new UTF8Encoding(false));
            }).ConfigureAwait(true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"The word could not be written to {file}: {e.Message}";
        }
    }

    private async Task LoadAsync(string? file, Action<Words> set)
    {
        if (file is null)
        {
            return;
        }

        var words = await Task.Run(() =>
        {
            try
            {
                return File.Exists(file) ? Words.Of(File.ReadAllLines(file)) : Words.Empty;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return Words.Empty;
            }
        }).ConfigureAwait(false);
        if (words.Set.Count > 0)
        {
            set(words);
            Bump();
        }
    }

    // What the checker knows changed: every editor it serves is drawn again (on the UI thread).
    private void Bump()
    {
        Interlocked.Increment(ref _version);
        void Redraw()
        {
            List<EditorModel> alive = [];
            lock (_editors)
            {
                foreach (var weak in _editors)
                {
                    if (weak.TryGetTarget(out var editor) && !editor.IsDisposed)
                    {
                        alive.Add(editor);
                    }
                }
            }

            foreach (var editor in alive)
            {
                editor.SpellingChanged();
            }
        }

        // Without a dispatcher (a headless host) nothing is redrawn from here: the next frame reads the new version.
        Post?.Invoke(Redraw);
    }

    // An immutable set of words ignoring case, with its span lookup.
    private sealed class Words
    {
        private Words(FrozenSet<string> set)
        {
            Set = set;
            Lookup = set.GetAlternateLookup<ReadOnlySpan<char>>();
        }

        public static Words Empty { get; } = new(FrozenSet<string>.Empty.ToFrozenSet(StringComparer.OrdinalIgnoreCase));

        public FrozenSet<string> Set { get; }

        public FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> Lookup { get; }

        public static Words Of(IEnumerable<string> lines) =>
            new(lines.Select(l => l.Trim()).Where(l => l.Length > 0 && l[0] != '#').ToFrozenSet(StringComparer.OrdinalIgnoreCase));

        public Words With(string word) => new(Set.Append(word).ToFrozenSet(StringComparer.OrdinalIgnoreCase));
    }
}
