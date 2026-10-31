// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;

namespace Craft.Core.Workspaces;

/// <summary>What a recent entry is.</summary>
public enum RecentKind
{
    /// <summary>A file.</summary>
    File,

    /// <summary>A folder (a workspace).</summary>
    Folder,
}

/// <summary>A recent file or folder.</summary>
/// <param name="Path">Its full path.</param>
/// <param name="Kind">A file or a folder.</param>
/// <param name="Opened">When it was last opened.</param>
public sealed record RecentEntry(string Path, RecentKind Kind, DateTimeOffset Opened);

/// <summary>
/// The recent files and folders, most recent first, at most <see cref="Limit"/> (<see cref="CraftOptions.RecentLimit"/>;
/// 0 keeps none), kept in a JSON file under the app's data folder (<c>recent.json</c>), written on every change. A path
/// opened again moves to the front; paths compare ignoring case where the file system does (Windows, macOS). A file that
/// does not parse starts the list empty and says why in <see cref="Problems"/>. Used on the UI thread.
/// </summary>
public sealed class RecentList
{
    /// <summary>The name of the file in the data folder.</summary>
    public const string FileName = "recent.json";

    private readonly List<RecentEntry> _entries = [];
    private readonly List<string> _problems = [];
    private int _limit;

    private RecentList(string path, int limit)
    {
        FilePath = Path.GetFullPath(path);
        _limit = Math.Max(0, limit);
    }

    /// <summary>The file, as a full path.</summary>
    public string FilePath { get; }

    /// <summary>The entries, most recent first.</summary>
    public IReadOnlyList<RecentEntry> Entries => _entries;

    /// <summary>What was wrong with the file when it was read; empty when nothing was.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The entries kept; lowering it drops the oldest (and saves).</summary>
    public int Limit
    {
        get => _limit;
        set
        {
            int limit = Math.Max(0, value);
            if (limit == _limit)
            {
                return;
            }

            _limit = limit;
            if (Trim())
            {
                Save();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Raised after the entries changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The list in the file at <paramref name="path"/> (empty when there is none), keeping at most <paramref name="limit"/>.</summary>
    public static RecentList Load(string path, int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var list = new RecentList(path, limit);
        try
        {
            if (File.Exists(list.FilePath))
            {
                list.Read(File.ReadAllBytes(list.FilePath));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            list._problems.Add($"{list.FilePath} could not be read: {e.Message}");
        }

        return list;
    }

    /// <summary>The recent list in the data folder of <paramref name="options"/>.</summary>
    public static RecentList Load(CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Load(Path.Combine(options.ResolveDataFolder(), FileName), options.RecentLimit);
    }

    /// <summary>Puts <paramref name="path"/> first (opened at <paramref name="opened"/>, now when null) and saves.</summary>
    public void Add(string path, RecentKind kind, DateTimeOffset? opened = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (_limit == 0)
        {
            return;
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        int at = IndexOf(full);
        if (at >= 0)
        {
            _entries.RemoveAt(at);
        }

        _entries.Insert(0, new RecentEntry(full, kind, opened ?? DateTimeOffset.Now));
        Trim();
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes <paramref name="path"/>; false when it was not listed.</summary>
    public bool Remove(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        int at = IndexOf(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
        if (at < 0)
        {
            return false;
        }

        _entries.RemoveAt(at);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Removes the entries whose file or folder no longer exists; returns how many.</summary>
    public int RemoveMissing()
    {
        int removed = _entries.RemoveAll(e => e.Kind == RecentKind.File ? !File.Exists(e.Path) : !Directory.Exists(e.Path));
        if (removed > 0)
        {
            Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>Removes every entry.</summary>
    public void Clear()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        _entries.Clear();
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes the entries to <see cref="FilePath"/> (its folder made when missing), through a temporary file moved over it.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("entries");
            foreach (var entry in _entries)
            {
                writer.WriteStartObject();
                writer.WriteString("path", entry.Path);
                writer.WriteString("kind", entry.Kind == RecentKind.Folder ? "folder" : "file");
                writer.WriteString("opened", entry.Opened);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        File.Move(temporary, FilePath, overwrite: true);
    }

    private int IndexOf(string full)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (PathKeys.Comparer.Equals(_entries[i].Path, full))
            {
                return i;
            }
        }

        return -1;
    }

    private bool Trim()
    {
        if (_entries.Count <= _limit)
        {
            return false;
        }

        _entries.RemoveRange(_limit, _entries.Count - _limit);
        return true;
    }

    private void Read(ReadOnlySpan<byte> utf8)
    {
        if (utf8.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            utf8 = utf8[3..];
        }

        var entries = new List<RecentEntry>();
        try
        {
            using var json = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("entries", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                _problems.Add($"{FilePath} has no \"entries\" list; the recent list starts empty.");
                return;
            }

            int index = 0;
            foreach (var item in list.EnumerateArray())
            {
                index++;
                if (item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String && path.GetString() is { Length: > 0 } text
                    && Path.IsPathFullyQualified(text))
                {
                    var kind = item.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String && k.ValueEquals("folder"u8) ? RecentKind.Folder : RecentKind.File;
                    var opened = item.TryGetProperty("opened", out var o) && o.ValueKind == JsonValueKind.String && o.TryGetDateTimeOffset(out var at) ? at : DateTimeOffset.MinValue;
                    if (entries.TrueForAll(e => !PathKeys.Comparer.Equals(e.Path, text)))
                    {
                        entries.Add(new RecentEntry(text, kind, opened));
                    }
                }
                else
                {
                    _problems.Add(string.Create(CultureInfo.InvariantCulture, $"{FilePath}: entry {index} has no full \"path\"; skipped."));
                }
            }
        }
        catch (JsonException e)
        {
            _problems.Add($"{FilePath} does not parse ({e.Message}); the recent list starts empty.");
            return;
        }

        _entries.AddRange(entries);
        Trim();
    }
}

/// <summary>How paths compare: ignoring case where the file system does (Windows, macOS), exactly elsewhere.</summary>
public static class PathKeys
{
    /// <summary>The comparer of full paths on the running system.</summary>
    public static StringComparer Comparer { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
