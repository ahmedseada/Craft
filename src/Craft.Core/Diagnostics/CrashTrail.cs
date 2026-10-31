// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Craft.Abstractions;

namespace Craft.Core.Diagnostics;

/// <summary>What a line of the <see cref="CrashTrail"/> records.</summary>
public enum CrashTrailKind : byte
{
    /// <summary>A note of the app's own (the run started, a callback failed, the crash dialog showed).</summary>
    Note,

    /// <summary>The label of UI work that ran (the watchdog's labels).</summary>
    Label,

    /// <summary>A command that ran.</summary>
    Command,

    /// <summary>A key pressed: its name and modifiers, never the text typed.</summary>
    Key,

    /// <summary>A first-chance exception: its type, message and top frames (rate-limited).</summary>
    Exception,

    /// <summary>A line of the Output log.</summary>
    Output,
}

/// <summary>
/// The breadcrumb trail of a run (the owner's note 21): the last UI work, the commands, the key names (never the text
/// typed), the first-chance exceptions and the Output lines, flushed to a small ring of two files in the crash folder as
/// they happen, so whatever kills the process (a native fail-fast no handler sees), the next start reads what it did
/// last. Any thread records; nothing is written on the caller's thread: an entry is a struct put on a channel (no
/// allocation per key), and one reader off the UI thread formats and appends it. Consecutive repeats of an entry (the
/// same label at every frame) are written once with their count. The ring keeps at most
/// <see cref="CraftOptions.CrashTrailKilobytes"/>: when the current file passes half of it, it becomes the older file.
/// </summary>
public sealed class CrashTrail : IDisposable
{
    private const int ChannelCapacity = 4096;
    private const int MessageLimit = 300;
    private const int FramesKept = 6;
    private static readonly string[] KindNames = ["note ", "ui   ", "cmd  ", "key  ", "exc  ", "out  "];

    [ThreadStatic]
    private static bool t_inException;

    private readonly Channel<Entry> _channel;
    private readonly Task _writer;
    private readonly long _halfBytes;
    private readonly int _chunk;
    private readonly int _exceptionsPerSecond;
    private readonly StringBuilder _line = new(256);
    private FileStream? _file;
    private long _exceptionSecond;
    private int _exceptionsThisSecond;
    private int _exceptionsDropped;
    private int _dropped;
    private Entry _last;
    private int _repeats;
    private bool _disposed;

    /// <summary>
    /// A trail appending to <paramref name="path"/> (and its older half, <see cref="OlderPath"/>), at most
    /// <paramref name="kilobytes"/> in all, with at most <paramref name="exceptionsPerSecond"/> exceptions a second.
    /// </summary>
    public CrashTrail(string path, int kilobytes = 256, int exceptionsPerSecond = 20)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(kilobytes);
        ArgumentOutOfRangeException.ThrowIfNegative(exceptionsPerSecond);
        Path = System.IO.Path.GetFullPath(path);
        OlderPath = OlderOf(Path);
        _halfBytes = Math.Max(4096, kilobytes * 1024L / 2);
        _chunk = (int)Math.Min(4096, _halfBytes / 4);
        _exceptionsPerSecond = exceptionsPerSecond;
        _channel = Channel.CreateBounded<Entry>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,              // TryWrite says false when full: the entry is counted as dropped
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _writer = Task.Run(WriteLoopAsync);
    }

    /// <summary>The current file.</summary>
    public string Path { get; }

    /// <summary>The older half of the ring (the current file's earlier lines).</summary>
    public string OlderPath { get; }

    /// <summary>Entries not recorded because the channel was full (a burst faster than the disk).</summary>
    public int Dropped => Volatile.Read(ref _dropped);

    /// <summary>The trail's older file for <paramref name="path"/>.</summary>
    public static string OlderOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return System.IO.Path.ChangeExtension(path, ".1" + System.IO.Path.GetExtension(path));
    }

    /// <summary>The trail's lines kept at <paramref name="path"/>, the oldest first (the older half, then the current file); at most <paramref name="lastLines"/>.</summary>
    public static IReadOnlyList<string> Read(string path, int lastLines = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(path);
        var lines = new List<string>();
        foreach (var file in (ReadOnlySpan<string>)[OlderOf(path), path])
        {
            try
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (reader.ReadLine() is { } line)
                {
                    lines.Add(line);
                }
            }
#pragma warning disable CA1031 // A trail that cannot be read leaves its lines out of the ticket, never a failure.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        return lines.Count <= lastLines ? lines : lines.GetRange(lines.Count - lastLines, lastLines);
    }

    /// <summary>A note of the app's own.</summary>
    public void Note(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Put(new Entry(DateTime.UtcNow.Ticks, CrashTrailKind.Note, text, null, 0, 0));
    }

    /// <summary>The label of UI work that starts (a string the caller already holds: nothing is allocated).</summary>
    public void Label(string label) => Put(new Entry(DateTime.UtcNow.Ticks, CrashTrailKind.Label, label, null, 0, 0));

    /// <summary>A command that runs.</summary>
    public void Command(string commandId) => Put(new Entry(DateTime.UtcNow.Ticks, CrashTrailKind.Command, commandId, null, 0, 0));

    /// <summary>A key pressed: its name and modifiers only (formatted by the writer, nothing allocated here).</summary>
    public void Key(EditorKey key, KeyModifiers modifiers) => Put(new Entry(DateTime.UtcNow.Ticks, CrashTrailKind.Key, null, null, (int)key, (int)modifiers));

    /// <summary>A line of the Output log.</summary>
    public void Output(string level, string source, string message) => Put(new Entry(DateTime.UtcNow.Ticks, CrashTrailKind.Output, message, source + " " + level, 0, 0));

    /// <summary>
    /// A first-chance exception (<see cref="AppDomain.FirstChanceException"/>, on the thread that threw): its type, message
    /// and the top frames of the throwing thread, at most the trail's exceptions a second (the rest are counted). A
    /// cancellation (<see cref="OperationCanceledException"/>) is not a fault and is left out; an exception thrown while
    /// one is recorded is not recorded.
    /// </summary>
    public void Exception(Exception error)
    {
        if (error is null or OperationCanceledException || t_inException || _exceptionsPerSecond == 0 || Volatile.Read(ref _disposed))
        {
            return;
        }

        long second = Environment.TickCount64 / 1000;
        if (Interlocked.Exchange(ref _exceptionSecond, second) != second)
        {
            Volatile.Write(ref _exceptionsThisSecond, 0);
        }

        if (Interlocked.Increment(ref _exceptionsThisSecond) > _exceptionsPerSecond)
        {
            Interlocked.Increment(ref _exceptionsDropped);
            return;
        }

        t_inException = true;
        try
        {
            var message = error.Message.ReplaceLineEndings(" ");
            var text = new StringBuilder(256).Append(error.GetType().FullName).Append(": ").Append(message.Length > MessageLimit ? message.AsSpan(0, MessageLimit) : message);
            // The throwing thread's stack (this handler runs on it, above the throw): its top frames, the handler's own skipped.
            var frames = new StackTrace(2, false).GetFrames();
            int kept = 0;
            foreach (var frame in frames)
            {
                if (DiagnosticMethodInfo.Create(frame) is not { } method || method.DeclaringTypeName == typeof(CrashTrail).FullName)
                {
                    continue;
                }

                text.Append(" | at ").Append(method.DeclaringTypeName).Append('.').Append(method.Name);
                if (++kept == FramesKept)
                {
                    break;
                }
            }

            Put(new Entry(DateTime.UtcNow.Ticks, CrashTrailKind.Exception, text.ToString(), null, 0, 0));
        }
#pragma warning disable CA1031 // Recording an exception never throws one of its own.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
        finally
        {
            t_inException = false;
        }
    }

    /// <summary>Completes when every entry recorded before the call is on the disk (the crash dialog waits on it, bounded).</summary>
    public async Task FlushAsync()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // Waits for room when the channel is full: a flush is never dropped.
            await _channel.Writer.WriteAsync(new Entry(0, CrashTrailKind.Note, null, null, 0, 0) { Flushed = done }).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return;                                                  // disposed: what was recorded is written already
        }

        await done.Task.ConfigureAwait(false);
    }

    /// <summary>Writes what is left and closes the file (the clean exit); waits at most 2 s.</summary>
    public void Dispose() => Dispose(null);

    /// <summary>Writes what is left and closes the file; waits at most <paramref name="timeout"/> (2 s when null).</summary>
    public void Dispose(TimeSpan? timeout)
    {
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        Volatile.Write(ref _disposed, true);
        _channel.Writer.TryComplete();
        try
        {
            _writer.Wait(timeout ?? TimeSpan.FromSeconds(2));
        }
#pragma warning disable CA1031 // A trail that cannot finish leaves what it wrote.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private void Put(in Entry entry)
    {
        if (!_channel.Writer.TryWrite(entry))
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    // The one reader: formats the entries waiting, appends them, flushes to the system after each batch.
    private async Task WriteLoopAsync()
    {
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                List<TaskCompletionSource>? flushed = null;
                _line.Clear();
                while (reader.TryRead(out var entry))
                {
                    if (entry.Flushed is { } done)
                    {
                        (flushed ??= []).Add(done);
                        continue;
                    }

                    Format(entry);
                    if (_line.Length >= _chunk)
                    {
                        Append();                                    // a long batch is written in pieces, so the ring turns within it
                    }
                }

                if (Interlocked.Exchange(ref _exceptionsDropped, 0) is > 0 and var skipped)
                {
                    FlushRepeats();
                    AppendLine(DateTime.UtcNow.Ticks, CrashTrailKind.Note, string.Create(CultureInfo.InvariantCulture, $"{skipped} more first-chance exceptions were not recorded (the trail's limit a second)"));
                }

                if (flushed is not null)
                {
                    FlushRepeats();
                }

                Append();
                if (flushed is not null)
                {
                    foreach (var done in flushed)
                    {
                        done.TrySetResult();
                    }
                }
            }

            FlushRepeats();
            Append();
        }
#pragma warning disable CA1031 // A trail that cannot be written stops; the app goes on.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
        finally
        {
            CloseFile();
            while (reader.TryRead(out var entry))
            {
                entry.Flushed?.TrySetResult();
            }
        }
    }

    private void Format(in Entry entry)
    {
        if (_repeats >= 0 && _last.Ticks != 0 && entry.Kind is CrashTrailKind.Label or CrashTrailKind.Key && entry.SameAs(_last))
        {
            _repeats++;
            return;
        }

        FlushRepeats();
        _last = entry;
        string text = entry.Kind switch
        {
            CrashTrailKind.Key => new KeyChord((EditorKey)entry.A, (KeyModifiers)entry.B).DisplayText,
            CrashTrailKind.Output => string.Concat(entry.Second, ": ", entry.Text),
            _ => entry.Text ?? "",
        };
        AppendLine(entry.Ticks, entry.Kind, text);
    }

    private void FlushRepeats()
    {
        if (_repeats > 0)
        {
            _line.Append(CultureInfo.InvariantCulture, $"                        (again ×{_repeats})\n");
        }

        _repeats = 0;
        _last = default;
    }

    private void AppendLine(long ticks, CrashTrailKind kind, string text)
    {
        var time = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
        _line.Append(CultureInfo.InvariantCulture, $"{time:yyyy-MM-dd HH:mm:ss.fff} ").Append(KindNames[(int)kind]);
        foreach (var c in text)
        {
            _line.Append(c is '\r' or '\n' ? ' ' : c);
        }

        _line.Append('\n');
    }

    // The batch's text to the current file, the ring turned first when it passed its half.
    private void Append()
    {
        if (_line.Length == 0)
        {
            return;
        }

        int count = Encoding.UTF8.GetMaxByteCount(_line.Length);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            int length = 0;
            foreach (var chunk in _line.GetChunks())
            {
                length += Encoding.UTF8.GetBytes(chunk.Span, buffer.AsSpan(length));
            }

            var file = Open();
            if (file.Length + length > _halfBytes && file.Length > 0)
            {
                file.Dispose();
                _file = null;
                File.Move(Path, OlderPath, overwrite: true);
                file = Open();
            }

            file.Write(buffer, 0, length);
            file.Flush();                                             // to the system: a crash of the process loses nothing written
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            _line.Clear();
        }
    }

    private void CloseFile()
    {
        _file?.Dispose();
        _file = null;
    }

    private FileStream Open()
    {
        if (_file is null)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            _file = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1);
        }

        return _file;
    }

    private readonly record struct Entry(long Ticks, CrashTrailKind Kind, string? Text, string? Second, int A, int B)
    {
        public TaskCompletionSource? Flushed { get; init; }

        public bool SameAs(in Entry other) => Kind == other.Kind && A == other.A && B == other.B && ReferenceEquals(Text, other.Text);
    }
}
