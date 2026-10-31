// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Globalization;
using Craft.Abstractions;
using Craft.Plugins.Sample;
using Idrak.Abstraction.Plugins;

// How Craft finds the plug-in when it loads the folder: the assembly names its package.
[assembly: IdrakPluginPackage(typeof(SamplePackage))]

namespace Craft.Plugins.Sample;

/// <summary>The assembly's plug-in package: what Craft registers when it loads the folder.</summary>
public sealed class SamplePackage : IIdrakPluginPackage
{
    /// <inheritdoc />
    public string Name => "Craft.Plugins.Sample";

    /// <inheritdoc />
    public IReadOnlyList<IIdrakPlugin> All { get; } = [new SampleRegistration()];
}

/// <summary>
/// Registers the sample's parts, and takes each back when it is unregistered (removed or disabled): the editor plug-in,
/// its command and its key binding.
/// </summary>
public sealed class SampleRegistration() : IdrakPlugin(WordCountPlugin.PluginName)
{
    /// <summary>The key binding: Ctrl+Shift+W runs the command (a chord the editor does not use, so it does not override it).</summary>
    public static KeyBinding Binding { get; } = new(new KeyChord(EditorKey.W, KeyModifiers.Control | KeyModifiers.Shift), WordCountPlugin.CommandId);

    /// <inheritdoc />
    protected override void RegisterParts()
    {
        Part(() => EditorPlugins.Register(new WordCountPlugin()), () => EditorPlugins.Unregister(WordCountPlugin.PluginName));
        Part(() => EditorCommands.Register(WordCountPlugin.Command), () => EditorCommands.Unregister(WordCountPlugin.CommandId));
        Part(() => EditorKeyBindings.Register(Binding), () => EditorKeyBindings.Unregister(Binding.Chord));
    }
}

/// <summary>
/// The sample: a status item with the active document's word count, kept up to date as the documents change by counting
/// only the lines each change touched (never the whole text on a keystroke), and a command that shows the counts.
/// </summary>
public sealed class WordCountPlugin : IEditorPlugin
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "sample";

    /// <summary>The command's id.</summary>
    public const string CommandId = "sample.wordCount";

    /// <summary>The status item's id.</summary>
    public const string StatusId = "sample.wordCount";

    /// <summary>The setting that adds the characters to the status item.</summary>
    public const string ShowCharactersKey = "showCharacters";

    /// <summary>The command: shows the active document's words, characters and lines.</summary>
    public static IEditorCommand Command { get; } = new EditorCommand(CommandId, "Sample: Count words",
        static (host, _) =>
        {
            var document = host.ActiveDocument!;
            var snapshot = document.Document.Buffer.Current;
            int words = WordCounter.Count(snapshot, 0, snapshot.Length);
            var name = document.FilePath is { } path ? Path.GetFileName(path) : "untitled";
            return host.ShowMessageAsync("Word count", string.Create(CultureInfo.InvariantCulture,
                $"{name}: {words} words, {snapshot.Length} characters, {snapshot.LineCount} lines."));
        },
        static host => host.ActiveDocument is not null,
        "Shows the active document's words, characters and lines");

    /// <inheritdoc />
    public string Name => PluginName;

    /// <inheritdoc />
    public string Summary => "A word-count status item and the command sample.wordCount (Ctrl+Shift+W)";

    /// <inheritdoc />
    public IReadOnlyList<PluginSetting> Settings { get; } =
        [new PluginSetting(ShowCharactersKey, "false", "Whether the status item shows the characters too.")];

    /// <inheritdoc />
    public IDisposable Activate(IEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        bool showCharacters = bool.TryParse(host.Settings.Get(PluginName, ShowCharactersKey), out var show) && show;
        return new Session(host, showCharacters);
    }

    /// <summary>The plug-in in one host: the counts of its documents and its status item.</summary>
    private sealed class Session : IDisposable
    {
        private readonly IEditorHost _host;
        private readonly bool _showCharacters;
        private readonly Dictionary<IEditorDocument, int> _words = [];
        private readonly StatusItem _item = new(StatusId, "", StatusAlignment.Right, priority: 10, commandId: CommandId);
        private readonly IDisposable _shown;
        private int _shownWords = -1;
        private int _shownCharacters = -1;

        public Session(IEditorHost host, bool showCharacters)
        {
            _host = host;
            _showCharacters = showCharacters;
            _item.Tooltip = "Words in the active document (click to count again)";
            foreach (var document in host.Documents)
            {
                _words[document] = CountAll(document);
            }

            host.DocumentOpened += OnOpened;
            host.DocumentChanged += OnChanged;
            host.DocumentClosed += OnClosed;
            host.ActiveDocumentChanged += OnActiveChanged;
            _shown = host.AddStatusItem(_item);
            Show();
        }

        public void Dispose()
        {
            _host.DocumentOpened -= OnOpened;
            _host.DocumentChanged -= OnChanged;
            _host.DocumentClosed -= OnClosed;
            _host.ActiveDocumentChanged -= OnActiveChanged;
            _shown.Dispose();
            _words.Clear();
        }

        private static int CountAll(IEditorDocument document)
        {
            var snapshot = document.Document.Buffer.Current;
            return WordCounter.Count(snapshot, 0, snapshot.Length);
        }

        private void OnOpened(object? sender, EditorDocumentEventArgs e)
        {
            _words[e.Document] = CountAll(e.Document);
            Show();
        }

        private void OnClosed(object? sender, EditorDocumentEventArgs e)
        {
            _words.Remove(e.Document);
            Show();
        }

        private void OnActiveChanged(object? sender, EventArgs e) => Show();

        private void OnChanged(object? sender, EditorDocumentChangedEventArgs e)
        {
            if (!_words.TryGetValue(e.Document, out int words))
            {
                return;
            }

            _words[e.Document] = words + WordCounter.Delta(e.Change);
            if (ReferenceEquals(e.Document, _host.ActiveDocument))
            {
                Show();
            }
        }

        private void Show()
        {
            var active = _host.ActiveDocument;
            int words = active is not null && _words.TryGetValue(active, out int count) ? count : -1;
            int characters = _showCharacters && active is not null ? active.Document.Buffer.Current.Length : -1;
            if (words == _shownWords && characters == _shownCharacters)
            {
                return;                                              // nothing new to show: no string is made
            }

            _shownWords = words;
            _shownCharacters = characters;
            _item.Text = words < 0 ? ""
                : characters < 0 ? string.Create(CultureInfo.InvariantCulture, $"{words} {(words == 1 ? "word" : "words")}")
                : string.Create(CultureInfo.InvariantCulture, $"{words} {(words == 1 ? "word" : "words")}, {characters} chars");
        }
    }
}

/// <summary>
/// Counts words (runs of characters that are not white space) in a snapshot's range, read in pooled blocks (no string
/// of the text is made), and the change a text edit makes to a count, from the lines the edit touched alone.
/// </summary>
public static class WordCounter
{
    private const int Block = 4096;

    /// <summary>The words of <paramref name="snapshot"/> from <paramref name="start"/> for <paramref name="length"/> characters.</summary>
    public static int Count(ITextSnapshot snapshot, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var buffer = ArrayPool<char>.Shared.Rent(Math.Min(Block, Math.Max(1, length)));
        try
        {
            int words = 0;
            bool inWord = false;
            int end = start + length;
            for (int at = start; at < end;)
            {
                var block = buffer.AsSpan(0, Math.Min(buffer.Length, end - at));
                snapshot.CopyTo(at, block);
                foreach (char c in block)
                {
                    bool space = char.IsWhiteSpace(c);
                    if (!space && !inWord)
                    {
                        words++;
                    }

                    inWord = !space;
                }

                at += block.Length;
            }

            return words;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// How much <paramref name="change"/> changed the word count: the words of the lines it touched after it, less those
    /// before it. A word never spans a line break, so the lines outside the change keep their words.
    /// </summary>
    public static int Delta(TextChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var edits = change.Edits;
        if (edits.Count == 0)
        {
            return 0;
        }

        var before = change.Before;
        var after = change.After;
        int firstLine = before.GetLineIndex(edits[0].Start);
        int lastLine = before.GetLineIndex(edits[^1].End);
        int lastAfter = lastLine + after.LineCount - before.LineCount;
        return Lines(after, firstLine, lastAfter) - Lines(before, firstLine, lastLine);
    }

    private static int Lines(ITextSnapshot snapshot, int first, int last)
    {
        if (last < first)
        {
            return 0;
        }

        int start = snapshot.GetLine(first).Start;
        return Count(snapshot, start, snapshot.GetLine(last).End - start);
    }
}
