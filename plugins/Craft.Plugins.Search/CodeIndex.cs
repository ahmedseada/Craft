// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Idrak.Abstraction.Retrieval;
using Idrak.Text.Abstractions;

namespace Craft.Plugins.Search;

/// <summary>A file of the index: where it is, what its chunks were made from (its length and time), its chunks and their vectors.</summary>
/// <param name="Path">Its path in the workspace, '/'-separated.</param>
/// <param name="Length">Its length when indexed.</param>
/// <param name="Ticks">Its last write time (UTC ticks) when indexed.</param>
/// <param name="Chunks">Its chunks, in order.</param>
/// <param name="Vectors">Each chunk's vector, or null when vectors are off.</param>
public sealed record IndexedFile(string Path, long Length, long Ticks, IReadOnlyList<CodeChunk> Chunks, IReadOnlyList<float[]>? Vectors);

/// <summary>A search's hit: the chunk, its file (full path and the workspace's), and its score (BM25's, or the fused rank's).</summary>
public sealed record CodeHit(string FullPath, string RelativePath, CodeChunk Chunk, double Score);

/// <summary>What one build of the index did (the bench and the status line read it).</summary>
/// <param name="Files">Files indexed.</param>
/// <param name="Parsed">Files parsed this time (new or changed); the others' chunks were kept.</param>
/// <param name="Chunks">Chunks in the index.</param>
/// <param name="Embedded">Chunks embedded this time.</param>
/// <param name="Elapsed">The build's time, the save included.</param>
/// <param name="Bytes">The index file's size, or 0 when it is not kept on disk.</param>
public sealed record IndexBuild(int Files, int Parsed, int Chunks, int Embedded, TimeSpan Elapsed, long Bytes);

/// <summary>
/// The code search's index of one workspace folder. <see cref="BuildAsync"/> walks the folder (the setting's extensions,
/// its ignored folders and size cap left out), keeps the chunks and vectors of every file whose length and time did not
/// change, parses (in parallel) and embeds only the others, then makes the keyword index over every chunk's words and the
/// vector index over their vectors, and swaps them in whole: a search meanwhile reads the previous ones. The result is
/// written to <see cref="IndexFile"/> (a binary file under the app's data folder, its name from the folder's path), read
/// back by <see cref="Load"/> at the next start. Builds run one at a time; searches run on any thread at once.
/// </summary>
public sealed class CodeIndex : IDisposable
{
    private const string Magic = "craft-search";
    private const int Version = 1;

    private readonly SemaphoreSlim _building = new(1, 1);
    private readonly Lazy<CodeEmbedder?> _embedder;
    private Snapshot? _current;
    private bool _disposed;

    /// <summary>An index of <paramref name="root"/> with <paramref name="settings"/>, kept at <paramref name="indexFile"/> (null: not kept).</summary>
    public CodeIndex(string root, SearchSettings settings, string? indexFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        IndexFile = indexFile;
        _embedder = new Lazy<CodeEmbedder?>(() => settings.EmbeddingModel.Length == 0 ? null : CodeEmbedder.Load(settings), LazyThreadSafetyMode.ExecutionAndPublication);
        Search = settings.EmbeddingModel.Length == 0 ? $"keywords ({settings.KeywordIndex})" : $"keywords ({settings.KeywordIndex}) and vectors ({settings.EmbeddingModel}), fused";
    }

    /// <summary>The workspace folder.</summary>
    public string Root { get; }

    /// <summary>The settings it was made with.</summary>
    public SearchSettings Settings { get; }

    /// <summary>Where it is kept, or null.</summary>
    public string? IndexFile { get; }

    /// <summary>How it searches, as the status line says it.</summary>
    public string Search { get; }

    /// <summary>Whether it holds an index (built or loaded).</summary>
    public bool IsReady => Volatile.Read(ref _current) is not null;

    /// <summary>The files indexed (none before the first build or load).</summary>
    public IReadOnlyList<IndexedFile> Files => Volatile.Read(ref _current)?.Files ?? [];

    /// <summary>The chunks indexed.</summary>
    public int ChunkCount => Volatile.Read(ref _current)?.Chunks.Length ?? 0;

    /// <summary>The last build, or null.</summary>
    public IndexBuild? LastBuild { get; private set; }

    /// <summary>The device the embedding model runs on, once it loaded; else null.</summary>
    public string? EmbeddingDevice => _embedder.IsValueCreated ? _embedder.Value?.Device : null;

    /// <summary>The file an index of <paramref name="root"/> is kept in, under <paramref name="folder"/>: a name from the folder's full path.</summary>
    public static string FileFor(string folder, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        string full = Path.GetFullPath(root);
        string key = OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        string name = new([.. Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Where(char.IsLetterOrDigit).Take(32)]);
        return Path.Combine(folder, $"{(name.Length > 0 ? name : "root")}-{hash}.index");
    }

    /// <summary>
    /// Reads the index kept at <see cref="IndexFile"/>; false when there is none, or it was made by another version, for
    /// another folder or under other settings (a build then makes it again).
    /// </summary>
    public bool Load()
    {
        if (IndexFile is null || !File.Exists(IndexFile))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(IndexFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadString() != Magic || reader.ReadInt32() != Version || reader.ReadString() != Root || reader.ReadString() != Settings.Signature)
            {
                return false;
            }

            int dimensions = reader.ReadInt32();
            int count = reader.ReadInt32();
            var files = new IndexedFile[count];
            for (int f = 0; f < count; f++)
            {
                string path = reader.ReadString();
                long length = reader.ReadInt64(), ticks = reader.ReadInt64();
                int chunks = reader.ReadInt32();
                var list = new CodeChunk[chunks];
                var vectors = dimensions > 0 ? new float[chunks][] : null;
                for (int c = 0; c < chunks; c++)
                {
                    list[c] = new CodeChunk(reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadString());
                    if (vectors is not null)
                    {
                        var vector = new float[dimensions];
                        reader.BaseStream.ReadExactly(System.Runtime.InteropServices.MemoryMarshal.AsBytes(vector.AsSpan()));
                        vectors[c] = vector;
                    }
                }

                files[f] = new IndexedFile(path, length, ticks, list, vectors);
            }

            Volatile.Write(ref _current, Snapshot.Make(files, Settings, dimensions));
            return true;
        }
        catch (Exception error) when (error is IOException or EndOfStreamException or FormatException or UnauthorizedAccessException)
        {
            return false;                                           // a cut or foreign file: built again
        }
    }

    /// <summary>
    /// Brings the index up to date with the folder (only new and changed files parsed and embedded), off the caller's thread;
    /// <paramref name="progress"/> gets the files parsed of those to parse. Builds run one at a time: a call while one runs
    /// waits for it, then looks again.
    /// </summary>
    public async Task<IndexBuild> BuildAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _building.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Build(progress, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _building.Release();
        }
    }

    /// <summary>Completes when a build or load has given the index something to search (at once when it has).</summary>
    public async Task WhenReadyAsync(CancellationToken cancellationToken = default)
    {
        if (IsReady)
        {
            return;
        }

        await _building.WaitAsync(cancellationToken).ConfigureAwait(false);
        _building.Release();
        if (!IsReady)
        {
            await BuildAsync(null, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The best <paramref name="top"/> chunks for <paramref name="query"/>: the keyword index's, fused with the vector
    /// index's by reciprocal rank when vectors are on; none before the index is ready. Any thread.
    /// </summary>
    public IReadOnlyList<CodeHit> Query(string query, int top)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Volatile.Read(ref _current) is not { } snapshot || query.AsSpan().Trim().IsEmpty || top <= 0)
        {
            return [];
        }

        int depth = Math.Max(top, Settings.FusionDepth);
        var keywords = snapshot.Keywords.Search(query, snapshot.Vectors is null ? top : depth);
        if (snapshot.Vectors is not { } vectors || _embedder.Value is not { } embedder)
        {
            return [.. keywords.Select(h => Hit(snapshot, h.Id, h.Score))];
        }

        var vector = embedder.Embed([query], 1)[0];
        var near = vectors.Search(vector, depth);
        var fused = new Dictionary<int, double>(keywords.Count + near.Count);
        for (int rank = 0; rank < keywords.Count; rank++)
        {
            fused[keywords[rank].Id] = 1.0 / (Settings.FusionK + rank + 1);
        }

        for (int rank = 0; rank < near.Count; rank++)
        {
            fused[near[rank].Id] = fused.GetValueOrDefault(near[rank].Id) + 1.0 / (Settings.FusionK + rank + 1);
        }

        return [.. fused.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(top).Select(p => Hit(snapshot, p.Key, p.Value))];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_embedder.IsValueCreated)
        {
            _embedder.Value?.Dispose();
        }
    }

    private CodeHit Hit(Snapshot snapshot, int id, double score)
    {
        var file = snapshot.Files[snapshot.FileOf[id]];
        return new CodeHit(Path.Combine(Root, file.Path.Replace('/', Path.DirectorySeparatorChar)), file.Path, snapshot.Chunks[id], score);
    }

    private IndexBuild Build(IProgress<(int Done, int Total)>? progress, CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        var old = Volatile.Read(ref _current);
        var kept = old?.Files.ToDictionary(f => f.Path, StringComparer.Ordinal) ?? new Dictionary<string, IndexedFile>(StringComparer.Ordinal);
        var embedder = _embedder.Value;
        var found = Walk(cancellationToken);
        var files = new IndexedFile?[found.Count];
        var parse = new List<int>();
        for (int i = 0; i < found.Count; i++)
        {
            var (path, length, ticks) = found[i];
            if (kept.TryGetValue(path, out var same) && same.Length == length && same.Ticks == ticks && (same.Vectors is not null) == (embedder is not null))
            {
                files[i] = same;
            }
            else
            {
                parse.Add(i);
            }
        }

        if (old is not null && parse.Count == 0 && found.Count == old.Files.Length)
        {
            return LastBuild = new IndexBuild(found.Count, 0, old.Chunks.Length, 0, watch.Elapsed, IndexFile is not null && File.Exists(IndexFile) ? new FileInfo(IndexFile).Length : 0);
        }

        // The new and changed files, parsed in parallel (each its own text and tree), then embedded in batches.
        var parsed = new List<ChunkText>?[found.Count];
        int done = 0;
        var parallel = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Settings.Threads > 0 ? Settings.Threads : Environment.ProcessorCount };
        Parallel.ForEach(parse, parallel, i =>
        {
            string full = Path.Combine(Root, found[i].Path.Replace('/', Path.DirectorySeparatorChar));
            string text;
            try
            {
                text = File.ReadAllText(full);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                text = "";                                          // gone or locked meanwhile: no chunks, looked at again next time
            }

            parsed[i] = CodeChunker.Chunk(found[i].Path, text, Settings, embedder is not null);
            int now = Interlocked.Increment(ref done);
            if (progress is not null && (now & 31) == 0)
            {
                progress.Report((now, parse.Count));
            }
        });
        progress?.Report((parse.Count, parse.Count));

        int embedded = 0;
        foreach (int i in parse)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunks = parsed[i]!;
            IReadOnlyList<float[]>? vectors = null;
            if (embedder is not null)
            {
                vectors = chunks.Count == 0 ? [] : embedder.Embed([.. chunks.Select(c => c.Text!)], Settings.EmbeddingBatch);
                embedded += chunks.Count;
            }

            files[i] = new IndexedFile(found[i].Path, found[i].Length, found[i].Ticks, [.. chunks.Select(c => c.Chunk)], vectors);
            parsed[i] = null;                                       // the embedding texts go as soon as they are used
        }

        var all = files.Select(f => f!).ToArray();
        int dimensions = embedder is null ? 0 : all.SelectMany(f => f.Vectors ?? []).FirstOrDefault()?.Length ?? embedder.Dimensions;
        var snapshot = Snapshot.Make(all, Settings, dimensions);
        Volatile.Write(ref _current, snapshot);
        long bytes = Save(snapshot);
        return LastBuild = new IndexBuild(all.Length, parse.Count, snapshot.Chunks.Length, embedded, watch.Elapsed, bytes);
    }

    // The folder's files the settings take, in path order, with their lengths and times; ignored folders are not entered.
    private List<(string Path, long Length, long Ticks)> Walk(CancellationToken cancellationToken)
    {
        var ignore = new HashSet<string>(Settings.Ignore, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var extensions = new HashSet<string>(Settings.Extensions, StringComparer.OrdinalIgnoreCase);
        long cap = Settings.MaxFileKilobytes * 1024L;
        var found = new List<(string, long, long)>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(Root));
        var enumeration = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = folder.EnumerateFileSystemInfos("*", enumeration);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (entry is DirectoryInfo sub)
                {
                    if (!ignore.Contains(sub.Name))
                    {
                        pending.Push(sub);
                    }
                }
                else if (entry is FileInfo file && extensions.Contains(file.Extension) && file.Length <= cap)
                {
                    found.Add((Path.GetRelativePath(Root, file.FullName).Replace(Path.DirectorySeparatorChar, '/'), file.Length, file.LastWriteTimeUtc.Ticks));
                }
            }
        }

        found.Sort(static (a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return found;
    }

    // The index file, written to a new file then moved over the old one (a cut write never leaves half an index); its size.
    private long Save(Snapshot snapshot)
    {
        if (IndexFile is null)
        {
            return 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(IndexFile)!);
        string temporary = IndexFile + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(Root);
            writer.Write(Settings.Signature);
            writer.Write(snapshot.Dimensions);
            writer.Write(snapshot.Files.Length);
            foreach (var file in snapshot.Files)
            {
                writer.Write(file.Path);
                writer.Write(file.Length);
                writer.Write(file.Ticks);
                writer.Write(file.Chunks.Count);
                for (int c = 0; c < file.Chunks.Count; c++)
                {
                    var chunk = file.Chunks[c];
                    writer.Write(chunk.Kind);
                    writer.Write(chunk.Name);
                    writer.Write(chunk.Container);
                    writer.Write(chunk.Line);
                    writer.Write(chunk.EndLine);
                    writer.Write(chunk.Terms);
                    if (snapshot.Dimensions > 0)
                    {
                        writer.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(file.Vectors![c].AsSpan()));
                    }
                }
            }
        }

        File.Move(temporary, IndexFile, overwrite: true);
        return new FileInfo(IndexFile).Length;
    }

    /// <summary>One whole index: the files, every chunk in order with its file, the keyword index and the vector index.</summary>
    private sealed class Snapshot(IndexedFile[] files, CodeChunk[] chunks, int[] fileOf, IKeywordIndex keywords, VectorIndex? vectors, int dimensions)
    {
        public IndexedFile[] Files { get; } = files;

        public CodeChunk[] Chunks { get; } = chunks;

        public int[] FileOf { get; } = fileOf;

        public IKeywordIndex Keywords { get; } = keywords;

        public VectorIndex? Vectors { get; } = vectors;

        public int Dimensions { get; } = dimensions;

        public static Snapshot Make(IndexedFile[] files, SearchSettings settings, int dimensions)
        {
            int total = files.Sum(f => f.Chunks.Count);
            var chunks = new CodeChunk[total];
            var fileOf = new int[total];
            var vectors = dimensions > 0 ? new VectorIndex(dimensions, VectorMetric.Cosine) : null;
            int n = 0;
            for (int f = 0; f < files.Length; f++)
            {
                for (int c = 0; c < files[f].Chunks.Count; c++)
                {
                    chunks[n] = files[f].Chunks[c];
                    fileOf[n++] = f;
                    if (vectors is not null)
                    {
                        vectors.Add(files[f].Vectors![c]);
                    }
                }
            }

            var keywords = KeywordIndexes.Create(settings.KeywordIndex, chunks.Select(c => c.Terms));
            return new Snapshot(files, chunks, fileOf, keywords, vectors, dimensions);
        }
    }
}
