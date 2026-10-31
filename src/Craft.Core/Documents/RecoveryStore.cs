// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Craft.Abstractions;

namespace Craft.Core.Documents;

/// <summary>A recovery copy found in the recovery folder: the document it was of, and when it was written.</summary>
/// <param name="Id">The copy's name in the folder.</param>
/// <param name="OriginalPath">The file the document was of; null for a document that was never saved.</param>
/// <param name="SavedAt">When the copy was written.</param>
/// <param name="CodePage">The code page of the document's encoding.</param>
/// <param name="HasBom">Whether the document's file had a byte order mark.</param>
/// <param name="LineEnding">The document's line ending style.</param>
/// <param name="Length">The copy's characters.</param>
public sealed record RecoveryEntry(string Id, string? OriginalPath, DateTimeOffset SavedAt, int CodePage, bool HasBom, string LineEnding, int Length);

/// <summary>
/// Autosave for crash recovery: a copy of each tracked dirty document is written under
/// <see cref="CraftOptions.RecoveryFolder"/> every <see cref="CraftOptions.AutosaveSeconds"/> (0 turns the timer off),
/// on a thread-pool thread, from an immutable snapshot, with a small JSON sidecar (the original path, the time, the
/// encoding). The copy of a document is deleted when it is saved (clean again) or closed. The file on disk is written
/// only by <see cref="ITextDocument.SaveAsync"/>, never here. After a crash, the next start lists the copies
/// (<see cref="ListRecoverable"/>), restores one (<see cref="Restore"/>) and discards it (<see cref="Discard"/>).
/// </summary>
public sealed class RecoveryStore : IDisposable
{
    private const string CopyExtension = ".txt";
    private const string SidecarExtension = ".json";

    private readonly CraftOptions _options;
    private readonly Lock _lock = new();
    private readonly List<Tracked> _tracked = [];
    private readonly Timer? _timer;
    private int _writing;
    private Task _stops = Task.CompletedTask;
    private bool _disposed;

    /// <summary>A store in <paramref name="options"/>' recovery folder (created when missing), its timer started when autosave is on.</summary>
    public RecoveryStore(CraftOptions? options = null)
    {
        _options = options ?? new CraftOptions();
        Folder = Path.GetFullPath(_options.RecoveryFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "Craft", "recovery"));
        Directory.CreateDirectory(Folder);
        if (_options.AutosaveSeconds > 0)
        {
            var period = TimeSpan.FromSeconds(_options.AutosaveSeconds);
            _timer = new Timer(static state => ((RecoveryStore)state!).Tick(), this, period, period);
        }
    }

    /// <summary>The folder the copies are kept in.</summary>
    public string Folder { get; }

    /// <summary>Starts keeping recovery copies of <paramref name="document"/>; dispose the result (or the document) to stop and delete its copy.</summary>
    public IDisposable Track(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tracked = new Tracked(this, document, Guid.NewGuid().ToString("N"));
        lock (_lock)
        {
            _tracked.Add(tracked);
        }

        document.StateChanged += tracked.OnStateChanged;
        document.Closed += tracked.OnClosed;
        return tracked;
    }

    /// <summary>Writes the copy of every tracked dirty document whose text changed since its last copy, and deletes those of clean ones, now (what the timer does).</summary>
    public async Task WriteNowAsync(CancellationToken cancellationToken = default)
    {
        await Task.Run(() => WriteAll(cancellationToken), cancellationToken).ConfigureAwait(false);
        Task stops;
        lock (_lock)
        {
            stops = _stops;
        }

        await stops.ConfigureAwait(false);                  // the copies of documents closed meanwhile are gone too
    }

    /// <summary>The copies in the folder that no tracked document of this store owns (the ones a crash left), newest first.</summary>
    public IReadOnlyList<RecoveryEntry> ListRecoverable()
    {
        HashSet<string> live;
        lock (_lock)
        {
            live = [.. _tracked.Select(t => t.Id)];
        }

        var entries = new List<RecoveryEntry>();
        foreach (var sidecar in Directory.EnumerateFiles(Folder, "*" + SidecarExtension))
        {
            var id = Path.GetFileNameWithoutExtension(sidecar);
            if (live.Contains(id) || !File.Exists(CopyPath(id)))
            {
                continue;
            }

            if (ReadSidecar(id, sidecar) is { } entry)
            {
                entries.Add(entry);
            }
        }

        entries.Sort((a, b) => b.SavedAt.CompareTo(a.SavedAt));
        return entries;
    }

    /// <summary>
    /// A document of <paramref name="entry"/>'s text, of its original file (or none) with its encoding and line ending,
    /// dirty until saved. The copy stays until <see cref="Discard"/>.
    /// </summary>
    public TextDocument Restore(RecoveryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var bytes = File.ReadAllBytes(CopyPath(entry.Id));
        var chars = new char[Encoding.UTF8.GetCharCount(bytes)];
        Encoding.UTF8.GetChars(bytes, chars);
        var encoding = Encoding.GetEncoding(entry.CodePage);
        return TextDocument.FromRecovery(chars, entry.OriginalPath, encoding, entry.HasBom, entry.LineEnding, _options);
    }

    /// <summary>Deletes <paramref name="entry"/>'s copy and sidecar.</summary>
    public void Discard(RecoveryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        DeleteFiles(entry.Id);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Dispose();
        Tracked[] all;
        lock (_lock)
        {
            all = [.. _tracked];
        }

        foreach (var tracked in all)
        {
            tracked.Stop(wait: true);              // the store closes with the app: its copies go now
        }
    }

    private void Tick()
    {
        try
        {
            WriteAll(CancellationToken.None);
        }
        catch (IOException)
        {
            // A copy that cannot be written now is tried again on the next tick.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void WriteAll(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _writing, 1) == 1)
        {
            return;
        }

        try
        {
            Tracked[] all;
            lock (_lock)
            {
                all = [.. _tracked];
            }

            foreach (var tracked in all)
            {
                cancellationToken.ThrowIfCancellationRequested();
                tracked.Sync();
            }
        }
        finally
        {
            Volatile.Write(ref _writing, 0);
        }
    }

    private string CopyPath(string id) => Path.Combine(Folder, id + CopyExtension);

    private string SidecarPath(string id) => Path.Combine(Folder, id + SidecarExtension);

    private void DeleteFiles(string id)
    {
        File.Delete(SidecarPath(id));
        File.Delete(CopyPath(id));
    }

    private void WriteCopy(string id, TextDocument document, ITextSnapshot snapshot)
    {
        // The copy is UTF-8 whatever the document's encoding (the sidecar keeps that), written as a save is: to a
        // temporary file, then moved over the last copy.
        TextDocument.WriteAtomically(snapshot, CopyPath(id), new UTF8Encoding(false), byteOrderMark: false);
        var sidecar = new System.Buffers.ArrayBufferWriter<byte>(512);
        using (var json = new Utf8JsonWriter(sidecar, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("originalPath", document.FilePath);
            json.WriteString("savedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            json.WriteNumber("codePage", document.Encoding.CodePage);
            json.WriteString("encoding", document.Encoding.WebName);
            json.WriteBoolean("hasBom", document.HasBom);
            json.WriteString("lineEnding", document.LineEnding);
            json.WriteNumber("length", snapshot.Length);
            json.WriteNumber("version", snapshot.Version);
            json.WriteEndObject();
        }

        var path = SidecarPath(id);
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, sidecar.WrittenSpan);
        File.Move(temporary, path, overwrite: true);
    }

    private static RecoveryEntry? ReadSidecar(string id, string path)
    {
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = json.RootElement;
            string? original = root.TryGetProperty("originalPath", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            var savedAt = DateTimeOffset.Parse(root.GetProperty("savedAt").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            return new RecoveryEntry(id, original, savedAt, root.GetProperty("codePage").GetInt32(), root.GetProperty("hasBom").GetBoolean(),
                root.GetProperty("lineEnding").GetString() ?? "\n", root.GetProperty("length").GetInt32());
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>A document the store follows: its copy's name and the version last copied.</summary>
    private sealed class Tracked(RecoveryStore store, TextDocument document, string id) : IDisposable
    {
        private readonly Lock _files = new();
        private int _copiedVersion = -1;
        private bool _hasCopy;
        private bool _stopped;

        public string Id { get; } = id;

        public void OnStateChanged(object? sender, EventArgs e)
        {
            if (!document.IsDirty && Volatile.Read(ref _hasCopy))
            {
                // Saved: the copy goes, off the UI thread.
                _ = Task.Run(Sync);
            }
        }

        public void OnClosed(object? sender, EventArgs e) => Dispose();

        /// <summary>Brings the copy in line with the document: written when dirty and changed, deleted when clean or stopped.</summary>
        public void Sync()
        {
            lock (_files)
            {
                var snapshot = document.Buffer.Current;
                if (!_stopped && document.IsDirty)
                {
                    if (snapshot.Version != _copiedVersion || !_hasCopy)
                    {
                        store.WriteCopy(Id, document, snapshot);
                        _copiedVersion = snapshot.Version;
                        Volatile.Write(ref _hasCopy, true);
                    }

                    return;
                }

                if (_hasCopy)
                {
                    store.DeleteFiles(Id);
                    _hasCopy = false;
                    _copiedVersion = -1;
                }
            }
        }

        /// <summary>Stops following the document; its copy goes off the calling thread (the UI's, when a tab closes): <see cref="WriteNowAsync"/> awaits it.</summary>
        public void Dispose() => Stop(wait: false);

        public void Stop(bool wait)
        {
            lock (store._lock)
            {
                if (!store._tracked.Remove(this))
                {
                    return;
                }
            }

            document.StateChanged -= OnStateChanged;
            document.Closed -= OnClosed;
            if (wait)
            {
                lock (_files)
                {
                    _stopped = true;
                }

                Sync();
                return;
            }

            // A copy being written now holds the files' lock for as long as the write takes: the closing thread never waits on it.
            lock (store._lock)
            {
                store._stops = Task.WhenAll(store._stops, Task.Run(() =>
                {
                    lock (_files)
                    {
                        _stopped = true;
                    }

                    try
                    {
                        Sync();
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        // A copy that cannot be deleted now is listed as recoverable at the next start.
                    }
                }));
            }
        }
    }
}
