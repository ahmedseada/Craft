// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json;
using Craft.Abstractions;
using Craft.Core.Workspaces;

namespace Craft.Editor.Shell;

/// <summary>A bookmark (X part 9): its file, its line (from 0), its label (or null), and its line's text as last read.</summary>
/// <param name="Path">The file.</param>
/// <param name="Line">The line, from 0.</param>
/// <param name="Label">The user's label, or null.</param>
/// <param name="Text">The line's text (trimmed, shortened) when it was last read.</param>
public sealed record Bookmark(string Path, int Line, string? Label, string Text);

/// <summary>
/// The bookmarks of a workspace (X part 9, item 13): lines of files the user marked, toggled at the caret, gone through
/// with Next and Previous, listed in the Bookmarks view, drawn in the gutter (<see cref="LineMarkKind.Bookmark"/> through
/// the document's line marks, as a plug-in's marks are) and kept in <c>.craft/bookmarks.json</c>. An open document's
/// bookmarks move with its edits (a line inserted above moves them down, a deleted line takes its bookmark to where the
/// deletion was; two that meet become one). Used on the UI thread; the file is read and written off it.
/// </summary>
public sealed class BookmarkList
{
    /// <summary>The line marks' source name of the bookmarks.</summary>
    public const string MarkSource = "bookmarks";

    private const int TextLimit = 120;
    private readonly Dictionary<string, List<Entry>> _byFile = new(PathKeys.Comparer);
    private readonly Dictionary<ITextDocument, Tracked> _tracked = new(ReferenceEqualityComparer.Instance);

    /// <summary>Raised after a bookmark was added, removed, labelled or moved.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised after a change the file should keep (a toggle, a label, a removal, a load is not one).</summary>
    public event EventHandler? Edited;

    /// <summary>How many bookmarks there are.</summary>
    public int Count
    {
        get
        {
            int count = 0;
            foreach (var list in _byFile.Values)
            {
                count += list.Count;
            }

            return count;
        }
    }

    /// <summary>Every bookmark, by file (in path order) then line.</summary>
    public IReadOnlyList<Bookmark> All()
    {
        var all = new List<Bookmark>(Count);
        foreach (var path in _byFile.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            all.AddRange(Of(path));
        }

        return all;
    }

    /// <summary>The bookmarks of <paramref name="path"/>, by line.</summary>
    public IReadOnlyList<Bookmark> Of(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        if (!_byFile.TryGetValue(full, out var list))
        {
            return [];
        }

        var snapshot = TrackedSnapshot(full);
        var result = new List<Bookmark>(list.Count);
        foreach (var entry in list)
        {
            result.Add(new Bookmark(full, entry.Line, entry.Label, snapshot is not null && entry.Line < snapshot.LineCount ? TextOf(snapshot, entry.Line) : entry.Text));
        }

        return result;
    }

    /// <summary>Whether <paramref name="path"/>'s line <paramref name="line"/> is bookmarked.</summary>
    public bool Has(string path, int line) => _byFile.TryGetValue(Path.GetFullPath(path), out var list) && list.Exists(e => e.Line == line);

    /// <summary>
    /// Adds a bookmark on <paramref name="path"/>'s line <paramref name="line"/>, or takes it away when there is one; true
    /// when it was added. <paramref name="text"/> gives the line's text (an open document's).
    /// </summary>
    public bool Toggle(string path, int line, ITextSnapshot? text = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        if (_byFile.TryGetValue(full, out var list) && list.FindIndex(e => e.Line == line) is >= 0 and var at)
        {
            list.RemoveAt(at);
            if (list.Count == 0)
            {
                _byFile.Remove(full);
            }

            Publish(full, edited: true);
            return false;
        }

        if (list is null)
        {
            _byFile[full] = list = [];
        }

        var snapshot = text ?? TrackedSnapshot(full);
        list.Add(new Entry(Math.Max(0, line), null, snapshot is not null && line < snapshot.LineCount ? TextOf(snapshot, line) : ""));
        list.Sort(static (a, b) => a.Line.CompareTo(b.Line));
        Publish(full, edited: true);
        return true;
    }

    /// <summary>Takes the bookmark on <paramref name="path"/>'s line <paramref name="line"/> away; false when there was none.</summary>
    public bool Remove(string path, int line) => Has(path, line) && !Toggle(path, line);

    /// <summary>Sets the label of the bookmark on <paramref name="path"/>'s line <paramref name="line"/> (empty or null takes it away); false when there is no bookmark there.</summary>
    public bool SetLabel(string path, int line, string? label)
    {
        var full = Path.GetFullPath(path);
        if (!_byFile.TryGetValue(full, out var list) || list.Find(e => e.Line == line) is not { } entry)
        {
            return false;
        }

        entry.Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        Publish(full, edited: true);
        return true;
    }

    /// <summary>Takes every bookmark away; the bookmarks taken (for Undo).</summary>
    public IReadOnlyList<Bookmark> Clear()
    {
        var all = All();
        var files = _byFile.Keys.ToArray();
        _byFile.Clear();
        foreach (var file in files)
        {
            Publish(file, edited: false);
        }

        Edited?.Invoke(this, EventArgs.Empty);
        return all;
    }

    /// <summary>Puts <paramref name="bookmarks"/> back (Undo of <see cref="Clear"/>) or in place of what is there (a load), each file's sorted.</summary>
    public void Set(IReadOnlyList<Bookmark> bookmarks, bool replace)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        var touched = new HashSet<string>(_byFile.Keys, PathKeys.Comparer);
        if (replace)
        {
            _byFile.Clear();
        }

        foreach (var bookmark in bookmarks)
        {
            var full = Path.GetFullPath(bookmark.Path);
            if (!_byFile.TryGetValue(full, out var list))
            {
                _byFile[full] = list = [];
            }

            if (!list.Exists(e => e.Line == bookmark.Line))
            {
                list.Add(new Entry(Math.Max(0, bookmark.Line), bookmark.Label, bookmark.Text));
            }

            touched.Add(full);
        }

        foreach (var list in _byFile.Values)
        {
            list.Sort(static (a, b) => a.Line.CompareTo(b.Line));
        }

        foreach (var file in touched)
        {
            Publish(file, edited: false);
        }

        if (!replace)
        {
            Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The bookmark after (or before, when not <paramref name="forward"/>) <paramref name="path"/>'s line
    /// <paramref name="line"/> across the files in <see cref="All"/>'s order; with <paramref name="wrap"/>, past the last
    /// the first. Null when there is none.
    /// </summary>
    public Bookmark? Next(string? path, int line, bool forward, bool wrap)
    {
        var all = All();
        if (all.Count == 0)
        {
            return null;
        }

        var full = path is null ? null : Path.GetFullPath(path);
        int Compare(Bookmark b)
        {
            int byFile = full is null ? 1 : string.Compare(b.Path, full, StringComparison.OrdinalIgnoreCase);
            return byFile != 0 ? byFile : b.Line.CompareTo(line);
        }

        if (forward)
        {
            foreach (var bookmark in all)
            {
                if (Compare(bookmark) > 0)
                {
                    return bookmark;
                }
            }

            return wrap ? all[0] : null;
        }

        for (int i = all.Count - 1; i >= 0; i--)
        {
            if (full is null || Compare(all[i]) < 0)
            {
                return all[i];
            }
        }

        return wrap ? all[^1] : null;
    }

    /// <summary>
    /// Follows <paramref name="document"/> (an open tab's): its bookmarks move with its edits and show in its gutter. Its
    /// file is read at each change, so a Save As follows; a document already followed is only re-keyed.
    /// </summary>
    public void Attach(ITextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_tracked.TryGetValue(document, out var tracked))
        {
            tracked.Path = document.FilePath is { } now ? Path.GetFullPath(now) : null;
            Marks(document, tracked.Path);
            return;
        }

        tracked = new Tracked(this, document);
        _tracked[document] = tracked;
        document.Buffer.Changed += tracked.OnChanged;
        Marks(document, tracked.Path);
    }

    /// <summary>Stops following <paramref name="document"/> (its last tab closed); its bookmarks stay at their lines.</summary>
    public void Detach(ITextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_tracked.Remove(document, out var tracked))
        {
            document.Buffer.Changed -= tracked.OnChanged;
            DocumentLineMarks.Of(document).Set(MarkSource, []);
        }
    }

    /// <summary>The documents followed.</summary>
    public IReadOnlyCollection<ITextDocument> Followed => _tracked.Keys;

    // An open document changed: each bookmark's line start mapped through the edits (an insertion at a line's start moves
    // it down with its text, a removal around it takes it to where the removal began), then its line in the new text.
    private void OnChanged(Tracked tracked, TextChangedEventArgs e)
    {
        if (tracked.Path is not { } path || !_byFile.TryGetValue(path, out var list) || list.Count == 0)
        {
            return;
        }

        bool moved = false;
        var before = e.Before;
        var after = e.After;
        foreach (var entry in list)
        {
            if (entry.Line >= before.LineCount)
            {
                continue;
            }

            int offset = before.GetLine(entry.Line).Start;
            int delta = 0;
            int mapped = -1;
            foreach (var edit in e.Edits)
            {
                if (edit.Start > offset || (edit.Start == offset && edit.Length > 0))
                {
                    break;
                }

                int end = edit.Start + edit.Length;
                if (end <= offset)
                {
                    delta += edit.Text.Length - edit.Length;
                }
                else
                {
                    mapped = edit.Start + delta;                    // inside a removed range: where it began
                    break;
                }
            }

            int position = Math.Clamp(mapped >= 0 ? mapped : offset + delta, 0, after.Length);
            int line = after.GetLineIndex(position);
            if (line != entry.Line)
            {
                entry.Line = line;
                moved = true;
            }
        }

        if (!moved)
        {
            return;
        }

        list.Sort(static (a, b) => a.Line.CompareTo(b.Line));
        for (int i = list.Count - 1; i > 0; i--)
        {
            if (list[i].Line == list[i - 1].Line)
            {
                list[i - 1].Label ??= list[i].Label;
                list.RemoveAt(i);                                   // two that met become one
            }
        }

        Publish(path, edited: false);
    }

    private void Publish(string path, bool edited)
    {
        foreach (var (document, tracked) in _tracked)
        {
            if (tracked.Path is { } followed && PathKeys.Comparer.Equals(followed, path))
            {
                Marks(document, path);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        if (edited)
        {
            Edited?.Invoke(this, EventArgs.Empty);
        }
    }

    // The document's gutter marks: one per bookmark, the line marks' bookmark kind.
    private void Marks(ITextDocument document, string? path)
    {
        var marks = new List<LineMark>();
        if (path is not null && _byFile.TryGetValue(path, out var list))
        {
            foreach (var entry in list)
            {
                marks.Add(new LineMark(entry.Line, 1, LineMarkKind.Bookmark));
            }
        }

        DocumentLineMarks.Of(document).Set(MarkSource, marks);
    }

    private ITextSnapshot? TrackedSnapshot(string full)
    {
        foreach (var (document, tracked) in _tracked)
        {
            if (tracked.Path is { } path && PathKeys.Comparer.Equals(path, full))
            {
                return document.Buffer.Current;
            }
        }

        return null;
    }

    private static string TextOf(ITextSnapshot snapshot, int line)
    {
        var text = snapshot.GetLine(line);
        int length = Math.Min(text.Length, TextLimit * 2);
        var read = snapshot.GetText(text.Start, length).AsSpan().Trim();
        return read.Length <= TextLimit ? read.ToString() : string.Concat(read[..(TextLimit - 1)], "…");
    }

    private sealed class Entry(int line, string? label, string text)
    {
        public int Line { get; set; } = line;

        public string? Label { get; set; } = label;

        public string Text { get; } = text;
    }

    private sealed class Tracked
    {
        private readonly BookmarkList _owner;
        private readonly ITextDocument _document;

        public Tracked(BookmarkList owner, ITextDocument document)
        {
            _owner = owner;
            _document = document;
            Path = document.FilePath is { } path ? System.IO.Path.GetFullPath(path) : null;
        }

        public string? Path { get; set; }

        public void OnChanged(object? sender, TextChangedEventArgs e) => _owner.OnChanged(this, e);
    }
}

/// <summary>
/// The workspace's bookmarks file (<c>.craft/bookmarks.json</c>): written on the thread pool, a later copy never overwritten
/// by an earlier one; read off the UI thread. Paths are kept relative to the workspace when inside it.
/// </summary>
public sealed class BookmarksFile
{
    /// <summary>The file's name in the workspace's <c>.craft</c> folder.</summary>
    public const string FileName = "bookmarks.json";

    private readonly Lock _writing = new();
    private long _version;
    private long _written;

    /// <summary>The file of the workspace <paramref name="root"/>.</summary>
    public BookmarksFile(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        FilePath = Path.Combine(Root, Core.Settings.CraftSettings.WorkspaceFolderName, FileName);
    }

    /// <summary>The workspace's folder.</summary>
    public string Root { get; }

    /// <summary>The file's full path.</summary>
    public string FilePath { get; }

    /// <summary>Writes <paramref name="bookmarks"/> on the thread pool (the latest call wins; none deletes the file); the task ends when it is on disk.</summary>
    public Task SaveAsync(IReadOnlyList<Bookmark> bookmarks)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        long version = Interlocked.Increment(ref _version);
        return Task.Run(() =>
        {
            lock (_writing)
            {
                if (version <= _written)
                {
                    return;
                }

                Write(bookmarks);
                _written = version;
            }
        });
    }

    /// <summary>The kept bookmarks; none when there is no file or it does not parse (call it off the UI thread).</summary>
    public IReadOnlyList<Bookmark> Load()
    {
        var bookmarks = new List<Bookmark>();
        try
        {
            if (!File.Exists(FilePath))
            {
                return bookmarks;
            }

            using var stream = File.OpenRead(FilePath);
            using var json = JsonDocument.Parse(stream);
            if (!json.RootElement.TryGetProperty("bookmarks", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return bookmarks;
            }

            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("file", out var file) && file.GetString() is { Length: > 0 } name
                    && item.TryGetProperty("line", out var line) && line.TryGetInt32(out int number) && number >= 1)
                {
                    bookmarks.Add(new Bookmark(Absolute(name), number - 1,
                        item.TryGetProperty("label", out var label) && label.GetString() is { Length: > 0 } text ? text : null,
                        item.TryGetProperty("text", out var preview) ? preview.GetString() ?? "" : ""));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException)
        {
            return [];
        }

        return bookmarks;
    }

    private void Write(IReadOnlyList<Bookmark> bookmarks)
    {
        if (bookmarks.Count == 0)
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }

            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", 1);
            writer.WriteStartArray("bookmarks");
            foreach (var bookmark in bookmarks)
            {
                writer.WriteStartObject();
                writer.WriteString("file", Relative(bookmark.Path));
                writer.WriteNumber("line", bookmark.Line + 1);
                if (bookmark.Label is { } label)
                {
                    writer.WriteString("label", label);
                }

                writer.WriteString("text", bookmark.Text);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        File.Move(temporary, FilePath, overwrite: true);
    }

    private string Relative(string path)
    {
        var full = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(Root, full);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? full : relative.Replace('\\', '/');
    }

    private string Absolute(string path) => Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(Root, path));
}
