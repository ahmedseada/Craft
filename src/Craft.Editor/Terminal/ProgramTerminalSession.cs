// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Craft.Abstractions;

namespace Craft.Editor.Terminal;

/// <summary>
/// The session behind a program's terminal tab (<see cref="IEditorTerminals.OpenProgram"/>, X part 6b): the program's
/// output and error streams are read in pooled chunks and handed to the terminal's reader in the order they arrive, with the
/// plug-in's notes (<see cref="Append"/>) and the echo of what is typed between them; what the user types goes to the
/// program's input through a terminal's line discipline, as a program reading a pipe expects it (the program has no
/// terminal, so the tab echoes and edits the line): printable text is echoed and kept in the line, Backspace takes its last
/// character back, Ctrl+U the whole line, Enter sends the line with "\n", Ctrl+D sends a started line without it and at
/// a line's start closes the input, Ctrl+C calls <see cref="EditorProgramRequest.Stop"/>; key sequences (arrows) are
/// dropped. A lone LF of the program's output is drawn as CR LF (ONLCR). Memory follows what is not yet drawn: at most
/// <see cref="MaxChunks"/> chunks of the program's output wait, its reads waiting after that.
/// </summary>
public sealed class ProgramTerminalSession : ITerminalSession
{
    /// <summary>The host name its terminals show ("program").</summary>
    public const string HostName = "program";

    /// <summary>How many chunks of the program's output may wait for the terminal's reader before the reads wait.</summary>
    public const int MaxChunks = 64;

    private const int ChunkBytes = 16 * 1024;
    private static readonly byte[] Erase = "\b \b"u8.ToArray();
    private readonly EditorProgramRequest _request;
    private readonly Channel<(byte[] Chunk, int Count, bool Counted)> _queue = Channel.CreateUnbounded<(byte[], int, bool)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _room = new(MaxChunks, MaxChunks);
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _lineGate = new();
    private readonly List<byte> _line = [];
    private byte[]? _pending;
    private int _pendingStart;
    private int _pendingCount;
    private bool _pendingCounted;
    private int _escape;
    private int _inputClosed;
    private int _ended;

    /// <summary>A session over <paramref name="request"/>'s streams; its <see cref="EditorProgramRequest.Header"/> first.</summary>
    public ProgramTerminalSession(EditorProgramRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        if (request.Header is { Length: > 0 } header)
        {
            Append(header);
        }

        var output = PumpAsync(request.Output);
        OutputEnded = request.Error is { } error ? Task.WhenAll(output, PumpAsync(error)) : output;
    }

    /// <inheritdoc />
    public int ProcessId => 0;

    /// <inheritdoc />
    public Task<int> Exited => _exited.Task;

    /// <summary>Completes once the program's output and error were read to their end.</summary>
    public Task OutputEnded { get; }

    /// <summary>Whether the tab still takes text and input (false once ended or disposed).</summary>
    public bool IsOpen => Volatile.Read(ref _ended) == 0;

    /// <summary>A note of the plug-in's between the program's output, each "\n" a line break; false once ended.</summary>
    public bool Append(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsOpen)
        {
            return false;
        }

        if (text.Length > 0)
        {
            Queue(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal)));
        }

        return true;
    }

    /// <summary>Shows <paramref name="text"/> last and ends the tab with <paramref name="exitCode"/>: nothing typed goes anywhere after it.</summary>
    public void End(string? text, int exitCode)
    {
        if (text is { Length: > 0 })
        {
            Append(text);
        }

        if (Interlocked.Exchange(ref _ended, 1) == 0)
        {
            _queue.Writer.TryComplete();
            CloseInput();
            _exited.TrySetResult(exitCode);
        }
    }

    /// <inheritdoc />
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_pending is { } pending)
            {
                int count = Math.Min(buffer.Length, _pendingCount);
                pending.AsMemory(_pendingStart, count).CopyTo(buffer);
                _pendingStart += count;
                _pendingCount -= count;
                if (_pendingCount == 0)
                {
                    _pending = null;
                    if (_pendingCounted)
                    {
                        ArrayPool<byte>.Shared.Return(pending);
                        _room.Release();
                    }
                }

                return count;
            }

            if (!await _queue.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return 0;
            }

            if (_queue.Reader.TryRead(out var next))
            {
                (_pending, _pendingCount, _pendingCounted) = next;
                _pendingStart = 0;
            }
        }
    }

    /// <inheritdoc />
    public void Write(ReadOnlySpan<byte> input)
    {
        if (!IsOpen)
        {
            return;
        }

        bool closed = Volatile.Read(ref _inputClosed) != 0;

        var echo = new ArrayBufferWriter<byte>(Math.Max(16, input.Length * 2));
        byte[]? send = null;
        bool stop = false, close = false;
        lock (_lineGate)
        {
            foreach (byte b in input)
            {
                if (closed && b != 0x03)
                {
                    continue;                                       // the input ended (Ctrl+D): only Ctrl+C still means something
                }

                if (_escape > 0)
                {
                    // A key's sequence (ESC [ … final, ESC O x): a line discipline has no use for it.
                    _escape = _escape == 1 ? (b is (byte)'[' or (byte)'O' ? 2 : 0) : (b is >= 0x40 and <= 0x7E ? 0 : 2);
                    continue;
                }

                switch (b)
                {
                    case 0x1B:
                        _escape = 1;
                        break;
                    case 0x03:                                          // Ctrl+C: the program is stopped (a pipe carries no signal)
                        _line.Clear();
                        echo.Write("^C\r\n"u8);
                        stop = true;
                        break;
                    case 0x04:                                          // Ctrl+D: a started line goes without its end; at a line's start, the input ends
                        if (_line.Count == 0)
                        {
                            close = true;
                        }
                        else
                        {
                            send = Join(send, _line);
                            _line.Clear();
                        }

                        break;
                    case (byte)'\r' or (byte)'\n':
                        _line.Add((byte)'\n');
                        send = Join(send, _line);
                        _line.Clear();
                        echo.Write("\r\n"u8);
                        break;
                    case 0x7F or 0x08:                                  // Backspace: the line's last character back (a UTF-8 sequence whole)
                        if (_line.Count > 0)
                        {
                            int at = _line.Count - 1;
                            while (at > 0 && (_line[at] & 0xC0) == 0x80)
                            {
                                at--;
                            }

                            _line.RemoveRange(at, _line.Count - at);
                            echo.Write(Erase);
                        }

                        break;
                    case 0x15:                                          // Ctrl+U: the whole line
                        for (int i = 0; i < _line.Count; i++)
                        {
                            if ((_line[i] & 0xC0) != 0x80)
                            {
                                echo.Write(Erase);
                            }
                        }

                        _line.Clear();
                        break;
                    case (byte)'\t':
                        _line.Add(b);
                        echo.Write("\t"u8);
                        break;
                    default:
                        if (b >= 0x20)
                        {
                            _line.Add(b);
                            echo.Write([b]);
                        }

                        break;
                }

                if (stop || close)
                {
                    break;
                }
            }
        }

        if (echo.WrittenCount > 0)
        {
            Queue(echo.WrittenSpan.ToArray());
        }

        if (send is not null)
        {
            try
            {
                _request.Input.Write(send);
                _request.Input.Flush();
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException or NotSupportedException)
            {
                // The program closed its input (it is ending): what is typed now goes nowhere.
            }
        }

        if (close)
        {
            CloseInput();
        }

        if (stop)
        {
            _request.Stop?.Invoke();
        }
    }

    /// <inheritdoc />
    public void Resize(TerminalSize size)
    {
        // A pipe has no size.
    }

    /// <summary>The user closed the tab (or the terminals were disposed): a program still running is stopped.</summary>
    public void Dispose()
    {
        bool running = !OutputEnded.IsCompleted;
        End(null, -1);
        if (running)
        {
            _request.Stop?.Invoke();
        }
    }

    private static byte[] Join(byte[]? before, List<byte> line)
    {
        if (before is null)
        {
            return [.. line];
        }

        var joined = new byte[before.Length + line.Count];
        before.CopyTo(joined, 0);
        line.CopyTo(joined, before.Length);
        return joined;
    }

    private void CloseInput()
    {
        if (Interlocked.Exchange(ref _inputClosed, 1) != 0)
        {
            return;
        }

        try
        {
            _request.Input.Dispose();
        }
        catch (IOException)
        {
            // The program closed its end first.
        }
    }

    // A note or an echo: never waits (it is not the program's output, so it takes no room).
    private void Queue(byte[] bytes) => _queue.Writer.TryWrite((bytes, bytes.Length, false));

    // The program's output: read in pooled chunks, a lone LF as CR LF, at most MaxChunks waiting.
    private async Task PumpAsync(Stream stream)
    {
        bool lastWasReturn = false;
        while (true)
        {
            await _room.WaitAsync().ConfigureAwait(false);
            var chunk = ArrayPool<byte>.Shared.Rent(ChunkBytes);
            int read;
            try
            {
                read = await stream.ReadAsync(chunk.AsMemory(0, ChunkBytes)).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException)
            {
                read = 0;
            }

            if (read > 0)
            {
                (chunk, read) = LineFeedsAsNewLines(chunk, read, ref lastWasReturn);
            }

            if (read == 0 || !_queue.Writer.TryWrite((chunk, read, true)))
            {
                ArrayPool<byte>.Shared.Return(chunk);
                _room.Release();
                return;
            }
        }
    }

    /// <summary>A lone LF as CR LF, as a terminal's line discipline gives the screen (ONLCR); a wider rented chunk when it has any.</summary>
    private static (byte[] Chunk, int Count) LineFeedsAsNewLines(byte[] chunk, int count, ref bool lastWasReturn)
    {
        var bytes = chunk.AsSpan(0, count);
        int lone = 0;
        bool previous = lastWasReturn;
        foreach (var b in bytes)
        {
            if (b == (byte)'\n' && !previous)
            {
                lone++;
            }

            previous = b == (byte)'\r';
        }

        if (lone == 0)
        {
            lastWasReturn = previous;
            return (chunk, count);
        }

        var wider = ArrayPool<byte>.Shared.Rent(count + lone);
        int at = 0;
        previous = lastWasReturn;
        foreach (var b in bytes)
        {
            if (b == (byte)'\n' && !previous)
            {
                wider[at++] = (byte)'\r';
            }

            wider[at++] = b;
            previous = b == (byte)'\r';
        }

        lastWasReturn = previous;
        ArrayPool<byte>.Shared.Return(chunk);
        return (wider, at);
    }
}
