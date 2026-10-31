// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Diagnostics;
using System.Text;
using Craft.Abstractions;
using Craft.Core.Text;

namespace Craft.Core.Documents;

/// <summary>
/// A document over a <see cref="PieceTableBuffer"/>: its file, its encoding (kept on save, with or without its byte
/// order mark), its line ending style, whether it is saved, and its undo history. The history stores the edits only: each
/// step keeps, per edit, its place and the text that is not in the buffer (the removed text while the step is done, the
/// inserted text while it is undone), and reads the other side from the buffer when it is undone or redone. Used on the
/// UI thread; <see cref="SaveAsync"/> encodes an immutable snapshot, so typing may go on while it writes.
/// </summary>
public sealed class TextDocument : ITextDocument
{
    private const int SaveBlockCharacters = 32 * 1024;

    private readonly PieceTableBuffer _buffer;
    private readonly CraftOptions _options;
    private readonly Func<long> _clock;
    private readonly List<Step> _undo = [];
    private readonly List<Step> _redo = [];
    private long _nextStepId = 1;
    private long _state;
    private long _savedState;
    private bool _disposed;

    private TextDocument(PieceTableBuffer buffer, CraftOptions? options, Func<long>? clock, string? path, Encoding encoding, bool hasBom, string lineEnding, bool fallback)
    {
        _buffer = buffer;
        _options = options ?? new CraftOptions();
        _clock = clock ?? DefaultClock;
        FilePath = path;
        Encoding = encoding;
        HasBom = hasBom;
        LineEnding = lineEnding;
        DecodedWithFallback = fallback;
    }

    /// <inheritdoc />
    public ITextBuffer Buffer => _buffer;

    /// <inheritdoc />
    public string? FilePath { get; private set; }

    /// <summary>The encoding the file was read with and is saved with (UTF-8 for a new document).</summary>
    public Encoding Encoding { get; private set; }

    /// <summary>Whether the file starts with a byte order mark, and is saved with one.</summary>
    public bool HasBom { get; private set; }

    /// <summary>
    /// The line break the file uses most ("\r\n" or "\n"), which the editor inserts on Enter; the text itself is kept as
    /// it is, mixed breaks and all. A file with no break, or a new document, takes <see cref="CraftOptions.NewLine"/>.
    /// </summary>
    public string LineEnding { get; set; }

    /// <summary>
    /// True when the file had no byte order mark and was not valid UTF-8, so it was read as Latin-1 (each byte one
    /// character, nothing lost); it is saved as Latin-1 too, unless <see cref="SetEncoding"/> changes that.
    /// </summary>
    public bool DecodedWithFallback { get; private set; }

    /// <inheritdoc />
    public bool IsDirty => FilePath is null ? _buffer.Current.Length > 0 : _state != _savedState;

    /// <inheritdoc />
    public bool CanUndo => _undo.Count > 0;

    /// <inheritdoc />
    public bool CanRedo => _redo.Count > 0;

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <summary>Raised when the document is disposed (closed).</summary>
    internal event EventHandler? Closed;

    /// <summary>A new document holding <paramref name="text"/>, with no file.</summary>
    /// <param name="text">Its text.</param>
    /// <param name="options">The settings (undo grouping, the add buffer's chunk, the new-document line break).</param>
    /// <param name="clock">A timestamp in milliseconds for undo grouping (tests pass a fake); null for the system's.</param>
    public static TextDocument Create(string text = "", CraftOptions? options = null, Func<long>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lineEnding = DominantLineEnding(text) ?? DefaultNewLine(options);
        return new TextDocument(new PieceTableBuffer(text, options), options, clock, null, Utf8NoBom, hasBom: false, lineEnding, fallback: false);
    }

    /// <summary>
    /// Reads <paramref name="path"/>: UTF-8 with or without a byte order mark, UTF-16 or UTF-32 by their marks, and a file
    /// with no mark that is not valid UTF-8 as Latin-1 (<see cref="DecodedWithFallback"/>). The text is kept as it is.
    /// </summary>
    public static async Task<TextDocument> OpenAsync(string path, CraftOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var bytes = await File.ReadAllBytesAsync(full, cancellationToken).ConfigureAwait(false);
        return FromBytes(bytes, full, options, clock: null);
    }

    /// <summary>A document read from <paramref name="bytes"/> as <see cref="OpenAsync"/> reads a file, its file being <paramref name="path"/>.</summary>
    internal static TextDocument FromBytes(ReadOnlySpan<byte> bytes, string? path, CraftOptions? options, Func<long>? clock)
    {
        var (encoding, bom, fallback) = Detect(bytes);
        var body = bytes[bom..];
        var chars = new char[encoding.GetCharCount(body)];
        encoding.GetChars(body, chars);
        var lineEnding = DominantLineEnding(chars) ?? DefaultNewLine(options);
        return new TextDocument(PieceTableBuffer.FromOwnedArray(chars, options), options, clock, path, encoding, bom > 0, lineEnding, fallback);
    }

    /// <summary>A document of recovered text: its file <paramref name="path"/> (or none), dirty until it is saved.</summary>
    internal static TextDocument FromRecovery(char[] chars, string? path, Encoding encoding, bool hasBom, string lineEnding, CraftOptions? options)
    {
        var document = new TextDocument(PieceTableBuffer.FromOwnedArray(chars, options), options, null, path, encoding, hasBom, lineEnding, fallback: false);
        document._savedState = -1;
        return document;
    }

    /// <summary>Saves with <paramref name="encoding"/> from now on (with a byte order mark when <paramref name="byteOrderMark"/>); the document becomes dirty.</summary>
    public void SetEncoding(Encoding encoding, bool byteOrderMark)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        Encoding = encoding;
        HasBom = byteOrderMark;
        DecodedWithFallback = false;
        _savedState = -1;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public ITextSnapshot Edit(ReadOnlySpan<TextEdit> edits, EditKind kind, SelectionState before, SelectionState after)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var current = _buffer.Current;
        long now = _clock();
        if (edits.Length == 1 && TryJoin(current, edits[0], kind, before, now, out var joined))
        {
            var applied = _buffer.Apply(edits);
            if (ReferenceEquals(applied, current))
            {
                return current;
            }

            joined.After = after;
            joined.Time = now;
            joined.Id = _nextStepId++;
            joined.Open = !BreaksGroup(kind, edits[0]);
            _state = joined.Id;
            _redo.Clear();
            StateChanged?.Invoke(this, EventArgs.Empty);
            return applied;
        }

        var history = new HistoryEdit[edits.Length];
        for (int i = 0; i < edits.Length; i++)
        {
            ref readonly var edit = ref edits[i];
            // Validated by Apply below; reading the removed text first needs the range inside the text.
            string removed = edit.Length == 0 || edit.Start < 0 || edit.Length < 0 || edit.Start > current.Length - edit.Length ? "" : current.GetText(edit.Start, edit.Length);
            history[i] = new HistoryEdit(edit.Start, edit.Length, edit.Text?.Length ?? 0, removed);
        }

        var result = _buffer.Apply(edits);
        if (ReferenceEquals(result, current))
        {
            return current;
        }

        if (_undo.Count > 0)
        {
            _undo[^1].Open = false;
        }

        var step = new Step(history, kind, before, after, now, _nextStepId++)
        {
            Open = (kind is EditKind.Typing or EditKind.Deleting) && edits.Length == 1 && !BreaksGroup(kind, edits[0]),
        };
        _undo.Add(step);
        _state = step.Id;
        _redo.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <inheritdoc />
    public SelectionState? Undo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_undo.Count == 0)
        {
            return null;
        }

        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        step.Open = false;
        var current = _buffer.Current;
        var edits = new TextEdit[step.Edits.Length];
        int shift = 0;
        for (int i = 0; i < edits.Length; i++)
        {
            ref var edit = ref step.Edits[i];
            int start = edit.Start + shift;
            string present = edit.AfterLength == 0 ? "" : current.GetText(start, edit.AfterLength);
            edits[i] = new TextEdit(start, edit.AfterLength, edit.Absent);
            edit.Absent = present;
            shift += edit.AfterLength - edit.BeforeLength;
        }

        _buffer.Apply(edits, TextChangeReason.Undo);
        _redo.Add(step);
        _state = _undo.Count > 0 ? _undo[^1].Id : 0;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return step.Before;
    }

    /// <inheritdoc />
    public SelectionState? Redo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_redo.Count == 0)
        {
            return null;
        }

        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        var current = _buffer.Current;
        var edits = new TextEdit[step.Edits.Length];
        for (int i = 0; i < edits.Length; i++)
        {
            ref var edit = ref step.Edits[i];
            string present = edit.BeforeLength == 0 ? "" : current.GetText(edit.Start, edit.BeforeLength);
            edits[i] = new TextEdit(edit.Start, edit.BeforeLength, edit.Absent);
            edit.Absent = present;
        }

        _buffer.Apply(edits, TextChangeReason.Redo);
        _undo.Add(step);
        _state = step.Id;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return step.After;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = path is not null ? Path.GetFullPath(path) : FilePath ?? throw new InvalidOperationException("The document has no file: give SaveAsync a path.");
        var snapshot = _buffer.Current;
        long state = _state;
        if (_undo.Count > 0)
        {
            _undo[^1].Open = false;
        }

        await WriteAtomicallyAsync(snapshot, target, Encoding, HasBom, cancellationToken).ConfigureAwait(false);
        FilePath = target;
        _savedState = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _undo.Clear();
        _redo.Clear();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Writes <paramref name="snapshot"/> to <paramref name="path"/> in <paramref name="encoding"/>: into a temporary file
    /// in the same folder, flushed to disk, then moved over the target, so the target is either the old file or the new
    /// one, never half written. A character the encoding cannot hold fails the save (nothing is replaced silently).
    /// </summary>
    internal static async Task WriteAtomicallyAsync(ITextSnapshot snapshot, string path, Encoding encoding, bool byteOrderMark, CancellationToken cancellationToken)
    {
        var folder = Path.GetDirectoryName(path) ?? throw new ArgumentException($"'{path}' has no folder.", nameof(path));
        var temporary = Path.Combine(folder, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".craft-save");
        var strict = Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
        var encoder = strict.GetEncoder();
        var chars = ArrayPool<char>.Shared.Rent(SaveBlockCharacters);
        var bytes = ArrayPool<byte>.Shared.Rent(strict.GetMaxByteCount(SaveBlockCharacters));
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 0, FileOptions.Asynchronous))
            {
                if (byteOrderMark)
                {
                    var preamble = PreambleOf(encoding);
                    await stream.WriteAsync(preamble, cancellationToken).ConfigureAwait(false);
                }

                int length = snapshot.Length;
                int position = 0;
                do
                {
                    int take = Math.Min(SaveBlockCharacters, length - position);
                    int written = EncodeBlock(snapshot, position, take, encoder, chars, bytes, flush: position + take == length);
                    await stream.WriteAsync(bytes.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
                    position += take;
                }
                while (position < length);

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                FlushToDisk(stream);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (EncoderFallbackException error)
        {
            TryDelete(temporary);
            throw new InvalidOperationException($"The text holds a character ({error.Message}) that {encoding.WebName} cannot hold; nothing was saved. Choose another encoding (SetEncoding) to save it.", error);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    /// <summary>
    /// <see cref="WriteAtomicallyAsync"/> on the calling thread, which is never the UI's (the recovery copies are written
    /// on the thread pool): the same temporary file, flush and move, without waiting on a task.
    /// </summary>
    internal static void WriteAtomically(ITextSnapshot snapshot, string path, Encoding encoding, bool byteOrderMark)
    {
        var folder = Path.GetDirectoryName(path) ?? throw new ArgumentException($"'{path}' has no folder.", nameof(path));
        var temporary = Path.Combine(folder, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".craft-save");
        var strict = Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
        var encoder = strict.GetEncoder();
        var chars = ArrayPool<char>.Shared.Rent(SaveBlockCharacters);
        var bytes = ArrayPool<byte>.Shared.Rent(strict.GetMaxByteCount(SaveBlockCharacters));
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 0))
            {
                if (byteOrderMark)
                {
                    stream.Write(PreambleOf(encoding));
                }

                int length = snapshot.Length;
                int position = 0;
                do
                {
                    int take = Math.Min(SaveBlockCharacters, length - position);
                    int written = EncodeBlock(snapshot, position, take, encoder, chars, bytes, flush: position + take == length);
                    stream.Write(bytes.AsSpan(0, written));
                    position += take;
                }
                while (position < length);

                stream.Flush();
                FlushToDisk(stream);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars);
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    /// <summary>The bytes of <paramref name="encoding"/>'s byte order mark (none for an encoding that has no mark).</summary>
    internal static byte[] PreambleOf(Encoding encoding) => encoding.CodePage switch
    {
        65001 => [0xEF, 0xBB, 0xBF],
        1200 => [0xFF, 0xFE],
        1201 => [0xFE, 0xFF],
        12000 => [0xFF, 0xFE, 0x00, 0x00],
        12001 => [0x00, 0x00, 0xFE, 0xFF],
        _ => encoding.GetPreamble(),
    };

    /// <summary>The encoding of <paramref name="bytes"/>, the length of its mark, and whether it fell back to Latin-1.</summary>
    internal static (Encoding Encoding, int Bom, bool Fallback) Detect(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            return (Utf8NoBom, 3, false);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]))
        {
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: false), 4, false);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
        {
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: false), 4, false);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: false), 2, false);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: false), 2, false);
        }

        return System.Text.Unicode.Utf8.IsValid(bytes) ? (Utf8NoBom, 0, false) : (Encoding.Latin1, 0, true);
    }

    /// <summary>"\r\n" or "\n", whichever <paramref name="text"/> holds more of (ties to "\r\n"), or null when it holds neither.</summary>
    internal static string? DominantLineEnding(ReadOnlySpan<char> text)
    {
        int crlf = 0, lf = 0, at = 0;
        while (true)
        {
            int found = text[at..].IndexOf('\n');
            if (found < 0)
            {
                break;
            }

            at += found;
            if (at > 0 && text[at - 1] == '\r')
            {
                crlf++;
            }
            else
            {
                lf++;
            }

            at++;
        }

        return crlf == 0 && lf == 0 ? null : crlf >= lf ? "\r\n" : "\n";
    }

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static long DefaultClock() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    private static string DefaultNewLine(CraftOptions? options) => options?.NewLine ?? Environment.NewLine;

    private static bool BreaksGroup(EditKind kind, in TextEdit edit) =>
        kind == EditKind.Typing && edit.Text is { } text && text.AsSpan().IndexOfAny('\r', '\n') >= 0;

    private static int EncodeBlock(ITextSnapshot snapshot, int start, int count, Encoder encoder, char[] chars, byte[] bytes, bool flush)
    {
        var block = chars.AsSpan(0, count);
        snapshot.CopyTo(start, block);
        return encoder.GetBytes(block, bytes, flush);
    }

    // The data reaches the disk before the move makes it the file (a sync call: there is no async form of it).
    private static void FlushToDisk(FileStream stream) => stream.Flush(flushToDisk: true);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Joins a single Typing or Deleting edit into the open step on top of the history: same kind, a short pause, the
    /// caret where the step left it, and the edit touching the step's range. On success the step's range and removed text
    /// already cover the edit (it is applied right after).
    /// </summary>
    private bool TryJoin(ITextSnapshot current, in TextEdit edit, EditKind kind, SelectionState before, long now, out Step step)
    {
        step = null!;
        if (kind is not (EditKind.Typing or EditKind.Deleting) || _undo.Count == 0 || edit.Text is null)
        {
            return false;
        }

        var top = _undo[^1];
        if (!top.Open || top.Kind != kind || top.Edits.Length != 1 || top.After != before || now - top.Time >= _options.UndoGroupMilliseconds || top.Id != _state)
        {
            return false;
        }

        if (edit.Start < 0 || edit.Length < 0 || edit.Start > current.Length - edit.Length)
        {
            return false;
        }

        ref var range = ref top.Edits[0];
        int start = range.Start, end = range.Start + range.AfterLength;
        if (edit.Start > end || edit.End < start)
        {
            return false;
        }

        int prefix = Math.Max(0, start - edit.Start);
        int suffix = Math.Max(0, edit.End - end);
        if (prefix > 0 || suffix > 0)
        {
            range.Absent = string.Concat(
                prefix > 0 ? current.GetText(edit.Start, prefix) : "",
                range.Absent,
                suffix > 0 ? current.GetText(end, suffix) : "");
        }

        int newStart = Math.Min(start, edit.Start);
        int span = Math.Max(end, edit.End) - newStart;
        range = new HistoryEdit(newStart, range.BeforeLength + prefix + suffix, span - edit.Length + edit.Text.Length, range.Absent);
        step = top;
        return true;
    }

    /// <summary>One edit of a step: where it starts (in the text before the step), its lengths before and after, and the side not in the buffer.</summary>
    private struct HistoryEdit(int start, int beforeLength, int afterLength, string absent)
    {
        public int Start = start;
        public int BeforeLength = beforeLength;
        public int AfterLength = afterLength;
        public string Absent = absent;
    }

    /// <summary>One undo step.</summary>
    private sealed class Step(HistoryEdit[] edits, EditKind kind, SelectionState before, SelectionState after, long time, long id)
    {
        public HistoryEdit[] Edits { get; } = edits;

        public EditKind Kind { get; } = kind;

        public SelectionState Before { get; } = before;

        public SelectionState After { get; set; } = after;

        public long Time { get; set; } = time;

        public long Id { get; set; } = id;

        public bool Open { get; set; }
    }
}
