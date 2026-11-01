// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Craft.Abstractions;

namespace Craft.Core.Documents;

/// <summary>A snapshot the local history keeps of a file: the file, the snapshot's own file, when it was saved, its size and its content's hash.</summary>
/// <param name="FilePath">The file it is a snapshot of.</param>
/// <param name="SnapshotPath">Where the snapshot's text is kept (UTF-8).</param>
/// <param name="Time">When the file was saved with this text.</param>
/// <param name="Bytes">The snapshot's size in bytes (UTF-8).</param>
/// <param name="Hash">The first 64 bits of its text's SHA-256, in hexadecimal.</param>
public sealed record LocalHistoryEntry(string FilePath, string SnapshotPath, DateTimeOffset Time, long Bytes, string Hash);

/// <summary>
/// The local history (X part 9, item 16): a snapshot of a file's text each time it is saved, kept in the user's data folder
/// (never the workspace), a folder per file (named by a hash of its path, its path written inside), each snapshot a UTF-8
/// file named by its time and its content's hash. A save whose text a kept snapshot of the file already has writes nothing
/// new: that snapshot takes the new time (deduplicated by hash). Bounded: snapshots older than <see cref="CraftOptions.LocalHistoryDays"/>
/// go, and past <see cref="CraftOptions.LocalHistoryMaxMegabytes"/> in all the oldest go; a file over
/// <see cref="CraftOptions.LocalHistoryFileMegabytes"/> is not kept. Everything here touches the disk: <see cref="AddAsync"/>
/// runs on the thread pool (one write at a time, in order); the reads are for the thread pool too. Its memory does not grow
/// with the saves: it holds the bytes kept in all and nothing per snapshot.
/// </summary>
public sealed class LocalHistory
{
    /// <summary>A snapshot's file extension.</summary>
    public const string SnapshotExtension = ".snap";

    /// <summary>The file in each file's folder that names the file.</summary>
    public const string SourceFileName = "source.txt";

    private const int Block = 16 * 1024;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly Lock _writing = new();
    private readonly TimeProvider _time;
    private long _kept = -1;
    private long _lastFullPrune;
    private Task _tail = Task.CompletedTask;
    private CraftOptions _options;

    /// <summary>The history in <paramref name="folder"/>, bounded by <paramref name="options"/>.</summary>
    public LocalHistory(string folder, CraftOptions options, TimeProvider? time = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(options);
        Folder = Path.GetFullPath(folder);
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The history of <paramref name="options"/>' data folder ("history" in it).</summary>
    public static LocalHistory InDataFolder(CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new LocalHistory(Path.Combine(options.ResolveDataFolder(), "history"), options);
    }

    /// <summary>The folder the snapshots are kept in.</summary>
    public string Folder { get; }

    /// <summary>The bounds in effect (the settings changed: the next write applies them).</summary>
    public CraftOptions Options
    {
        get => Volatile.Read(ref _options);
        set => Volatile.Write(ref _options, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Whether snapshots are kept (<see cref="CraftOptions.LocalHistoryMaxMegabytes"/> is not 0).</summary>
    public bool IsOn => Options.LocalHistoryMaxMegabytes > 0;

    /// <summary>The bytes kept in all, as last counted (-1 before the first write counted them).</summary>
    public long KeptBytes => Interlocked.Read(ref _kept);

    /// <summary>The last write (the tests and the shell's close wait on it).</summary>
    public Task Pending
    {
        get
        {
            lock (_writing)
            {
                return _tail;
            }
        }
    }

    /// <summary>The folder of <paramref name="filePath"/>'s snapshots.</summary>
    public string FolderOf(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var full = Path.GetFullPath(filePath);
        var key = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? full.ToUpperInvariant() : full;
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(key), hash);
        return Path.Combine(Folder, Convert.ToHexStringLower(hash[..12]));
    }

    /// <summary>
    /// Keeps <paramref name="text"/> as <paramref name="filePath"/>'s snapshot of now, on the thread pool after the writes
    /// before it: the snapshot written (or a kept one with the same text moved to now), then the bounds applied. The entry,
    /// or null when nothing was kept (the history is off, the file is over its size, or its newest snapshot has the text).
    /// </summary>
    public Task<LocalHistoryEntry?> AddAsync(string filePath, ITextSnapshot text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(text);
        var full = Path.GetFullPath(filePath);
        var options = Options;
        var now = _time.GetUtcNow();
        lock (_writing)
        {
            var previous = _tail;
            var write = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                return Add(full, text, options, now, cancellationToken);
            }, cancellationToken);
            _tail = write.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return write;
        }
    }

    /// <summary>The snapshots of <paramref name="filePath"/>, newest first (reads the disk: call it off the UI thread).</summary>
    public IReadOnlyList<LocalHistoryEntry> Entries(string filePath)
    {
        var full = Path.GetFullPath(filePath);
        var folder = FolderOf(full);
        var entries = new List<LocalHistoryEntry>();
        if (!Directory.Exists(folder))
        {
            return entries;
        }

        foreach (var info in new DirectoryInfo(folder).EnumerateFiles("*" + SnapshotExtension))
        {
            if (Parse(info.Name) is { } name)
            {
                entries.Add(new LocalHistoryEntry(full, info.FullName, new DateTimeOffset(name.Ticks, TimeSpan.Zero), info.Length, name.Hash));
            }
        }

        entries.Sort(static (a, b) => b.Time.CompareTo(a.Time));
        return entries;
    }

    /// <summary>A snapshot's text (reads the disk: call it off the UI thread).</summary>
    public static string Read(LocalHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return File.ReadAllText(entry.SnapshotPath, Utf8);
    }

    /// <summary>
    /// Applies the bounds now (what each write does): snapshots past the days go, then the oldest until the total is within
    /// the megabytes; the snapshots deleted. Reads the disk: call it off the thread pool's writer or a test.
    /// </summary>
    public int Prune()
    {
        lock (_writing)
        {
            return PruneCore(Options, null);
        }
    }

    // The write, on the thread pool, one at a time (the tail of the earlier ones awaited first).
    private LocalHistoryEntry? Add(string full, ITextSnapshot text, CraftOptions options, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (options.LocalHistoryMaxMegabytes <= 0)
        {
            return null;
        }

        long fileLimit = options.LocalHistoryFileMegabytes * 1024L * 1024L;
        if (text.Length > fileLimit)
        {
            return null;                                        // even at a byte a character it is over
        }

        var folder = FolderOf(full);
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, SourceFileName);
        if (!File.Exists(source))
        {
            File.WriteAllText(source, full, Utf8);
        }

        if (Interlocked.Read(ref _kept) < 0)
        {
            Interlocked.Exchange(ref _kept, Count());
        }

        var temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        long bytes;
        string hash;
        try
        {
            (bytes, hash) = WriteText(temporary, text, cancellationToken);
            if (bytes > fileLimit)
            {
                File.Delete(temporary);
                return null;
            }

            // The kept snapshots of the file: the newest with this text keeps it (nothing new); an older one moves to now.
            // Those past the days go here (the other files' go with the full walk, at most once an hour).
            long oldest = now.UtcTicks - TimeSpan.FromDays(Math.Max(1, options.LocalHistoryDays)).Ticks;
            string? same = null;
            long newest = long.MinValue;
            string? newestHash = null;
            foreach (var info in new DirectoryInfo(folder).EnumerateFiles("*" + SnapshotExtension))
            {
                var path = info.FullName;
                if (Parse(info.Name) is not { } name)
                {
                    continue;
                }

                if (name.Ticks < oldest)
                {
                    long size = info.Length;
                    if (TryDelete(path))
                    {
                        Interlocked.Add(ref _kept, -size);
                    }

                    continue;
                }

                if (name.Ticks > newest)
                {
                    newest = name.Ticks;
                    newestHash = name.Hash;
                }

                if (string.Equals(name.Hash, hash, StringComparison.Ordinal))
                {
                    same = path;
                }
            }

            if (string.Equals(newestHash, hash, StringComparison.Ordinal))
            {
                File.Delete(temporary);
                return null;
            }

            long ticks = Math.Max(now.UtcTicks, newest + 1);
            var target = Path.Combine(folder, NameOf(ticks, hash));
            if (same is not null)
            {
                File.Delete(temporary);
                File.Move(same, target);
            }
            else
            {
                File.Move(temporary, target);
                Interlocked.Add(ref _kept, bytes);
            }

            long limit = options.LocalHistoryMaxMegabytes * 1024L * 1024L;
            if (Interlocked.Read(ref _kept) > limit || now.UtcTicks - _lastFullPrune > TimeSpan.TicksPerHour)
            {
                _lastFullPrune = now.UtcTicks;
                PruneCore(options, target);
            }

            return new LocalHistoryEntry(full, target, new DateTimeOffset(ticks, TimeSpan.Zero), bytes, hash);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    // The text as UTF-8 in blocks (rule 74: bytes, a pooled block, never one string of the whole file), hashed as written.
    private static (long Bytes, string Hash) WriteText(string path, ITextSnapshot text, CancellationToken cancellationToken)
    {
        char[] chars = ArrayPool<char>.Shared.Rent(Block);
        byte[] bytes = ArrayPool<byte>.Shared.Rent(Utf8.GetMaxByteCount(Block));
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var encoder = Utf8.GetEncoder();
            long written = 0;
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.SequentialScan))
            {
                int length = text.Length;
                for (int at = 0; at < length; at += Block)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(Block, length - at);
                    text.CopyTo(at, chars.AsSpan(0, count));
                    int made = encoder.GetBytes(chars.AsSpan(0, count), bytes, flush: at + count >= length);
                    stream.Write(bytes, 0, made);
                    hash.AppendData(bytes, 0, made);
                    written += made;
                }
            }

            Span<byte> digest = stackalloc byte[32];
            hash.GetHashAndReset(digest);
            return (written, Convert.ToHexStringLower(digest[..8]));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    // The bounds: past the days, then the oldest past the megabytes (never `spare`, the snapshot just written).
    private int PruneCore(CraftOptions options, string? spare)
    {
        if (!Directory.Exists(Folder))
        {
            return 0;
        }

        long oldest = _time.GetUtcNow().UtcTicks - TimeSpan.FromDays(Math.Max(1, options.LocalHistoryDays)).Ticks;
        long limit = Math.Max(0, options.LocalHistoryMaxMegabytes) * 1024L * 1024L;
        int deleted = 0;
        long kept = 0;
        List<(string Path, long Ticks, long Bytes)>? all = null;
        foreach (var folder in Directory.EnumerateDirectories(Folder))
        {
            foreach (var info in new DirectoryInfo(folder).EnumerateFiles("*" + SnapshotExtension))
            {
                if (Parse(info.Name) is not { } name)
                {
                    continue;
                }

                if (name.Ticks < oldest && !string.Equals(info.FullName, spare, StringComparison.Ordinal))
                {
                    deleted += TryDelete(info.FullName) ? 1 : 0;
                    continue;
                }

                kept += info.Length;
                (all ??= []).Add((info.FullName, name.Ticks, info.Length));
            }
        }

        if (kept > limit && all is not null)
        {
            all.Sort(static (a, b) => a.Ticks.CompareTo(b.Ticks));
            foreach (var (path, _, size) in all)
            {
                if (kept <= limit)
                {
                    break;
                }

                if (!string.Equals(path, spare, StringComparison.Ordinal) && TryDelete(path))
                {
                    kept -= size;
                    deleted++;
                }
            }
        }

        Interlocked.Exchange(ref _kept, kept);
        return deleted;
    }

    // The bytes kept in all (the first write counts them once).
    private long Count()
    {
        long total = 0;
        if (!Directory.Exists(Folder))
        {
            return 0;
        }

        foreach (var folder in Directory.EnumerateDirectories(Folder))
        {
            foreach (var info in new DirectoryInfo(folder).EnumerateFiles("*" + SnapshotExtension))
            {
                total += info.Length;
            }
        }

        return total;
    }

    private static string NameOf(long ticks, string hash) => string.Create(CultureInfo.InvariantCulture, $"{ticks:D19}-{hash}{SnapshotExtension}");

    // "0638...-abcdef0123456789.snap": its time in UTC ticks and its hash; null for any other file.
    private static (long Ticks, string Hash)? Parse(string name)
    {
        var span = name.AsSpan();
        if (!span.EndsWith(SnapshotExtension, StringComparison.Ordinal))
        {
            return null;
        }

        span = span[..^SnapshotExtension.Length];
        int dash = span.IndexOf('-');
        if (dash <= 0 || !long.TryParse(span[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks) || span.Length - dash - 1 != 16)
        {
            return null;
        }

        return (ticks, span[(dash + 1)..].ToString());
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
