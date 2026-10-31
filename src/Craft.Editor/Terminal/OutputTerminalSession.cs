// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using System.Threading.Channels;
using Craft.Abstractions;

namespace Craft.Editor.Terminal;

/// <summary>
/// The session behind a read-only terminal tab (<see cref="IEditorTerminals.OpenOutput"/>): no process; the bytes a
/// plug-in appends are queued (from any thread) and read by the terminal's reader as if a shell wrote them. Input and
/// replies go nowhere. Each append is one queued array, so memory follows what is not yet drawn, never the history (the
/// screen's scrollback keeps that, capped).
/// </summary>
public sealed class OutputTerminalSession : ITerminalSession
{
    /// <summary>The host name its terminals show ("output").</summary>
    public const string HostName = "output";

    private readonly Channel<byte[]> _queue = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private byte[]? _pending;
    private int _pendingOffset;

    /// <inheritdoc />
    public int ProcessId => 0;

    /// <inheritdoc />
    public Task<int> Exited => _exited.Task;

    /// <summary>Whether it still takes text (false once disposed).</summary>
    public bool IsOpen => !_exited.Task.IsCompleted;

    /// <summary>Queues <paramref name="text"/> with each "\n" as a line break the screen understands ("\r\n"); false once closed.</summary>
    public bool Append(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return IsOpen;
        }

        var bytes = Encoding.UTF8.GetBytes(text.Contains('\n', StringComparison.Ordinal) ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal) : text);
        return _queue.Writer.TryWrite(bytes);
    }

    /// <inheritdoc />
    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_pending is null)
        {
            try
            {
                _pending = await _queue.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (ChannelClosedException)
            {
                return 0;
            }

            _pendingOffset = 0;
        }

        int count = Math.Min(buffer.Length, _pending.Length - _pendingOffset);
        _pending.AsSpan(_pendingOffset, count).CopyTo(buffer.Span);
        _pendingOffset += count;
        if (_pendingOffset == _pending.Length)
        {
            _pending = null;
        }

        return count;
    }

    /// <inheritdoc />
    public void Write(ReadOnlySpan<byte> input)
    {
        // Read-only: what is typed (and the screen's replies) goes nowhere.
    }

    /// <inheritdoc />
    public void Resize(TerminalSize size)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _exited.TrySetResult(0);
    }
}
