// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Globalization;
using System.Threading.Channels;
using Craft.Abstractions;

namespace Craft.Editor.Terminal;

/// <summary>
/// One terminal: its shell's session (<see cref="ITerminalSession"/>) and its screen. The output is read and parsed on a
/// thread-pool loop (never on the UI thread), in slices under the screen's lock, and the view is asked to redraw once per
/// burst (<see cref="Invalidated"/>, raised through the host's dispatch on the UI thread). Keys and text are encoded and
/// written to the shell off the calling thread (phase U: a shell that is not reading, its pipe full, never blocks the UI
/// thread, nor the parser that holds the screen's lock while it answers a query), in order; the view's scroll position,
/// its selection and the exit are kept here, UI-free.
/// </summary>
public sealed class TerminalInstance : IDisposable
{
    private const int ReadBytes = 64 * 1024;
    private const int SliceBytes = 16 * 1024;
    private readonly Action<Action> _dispatch;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<byte[]> _input = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly string? _name;
    private int _invalidatePending;
    private long _scrolledSeen;
    private int _suppressText = -1;
    private long _bytesRead;
    private int _disposed;
    private int _scrollBack;

    private TerminalInstance(int id, ITerminalSession session, TerminalScreen screen, TerminalShell shell, string folder, string hostName, string? name, Action<Action> dispatch)
    {
        Id = id;
        Session = session;
        Screen = screen;
        Shell = shell;
        WorkingDirectory = folder;
        HostName = hostName;
        _name = name;
        _dispatch = dispatch;
        screen.Respond = Write;
        screen.TitleChanged += (_, _) => _dispatch(() => TitleChanged?.Invoke(this, EventArgs.Empty));
        Completion = ReadAsync();
        Writes = WriteAsync();
    }

    /// <summary>The writer's loop: completes once the terminal is disposed and what was written before is sent.</summary>
    public Task Writes { get; }

    /// <summary>Its number in this run (1 for the first terminal).</summary>
    public int Id { get; }

    /// <summary>The shell's session.</summary>
    public ITerminalSession Session { get; }

    /// <summary>The screen (lock <see cref="TerminalScreen.Sync"/> to read it off the reader).</summary>
    public TerminalScreen Screen { get; }

    /// <summary>The shell it runs.</summary>
    public TerminalShell Shell { get; }

    /// <summary>The folder it started in.</summary>
    public string WorkingDirectory { get; }

    /// <summary>The terminal host that started it ("conpty", "pipe").</summary>
    public string HostName { get; }

    /// <summary>The tab's title: a name it was given, else the shell's own title, else the shell and the folder ("pwsh — Craft").</summary>
    public string Title
    {
        get
        {
            if (_name is not null)
            {
                return _name;
            }

            string? title;
            string? folder;
            lock (Screen.Sync)
            {
                title = Screen.Title;
                folder = Screen.WorkingDirectory;
            }

            return title ?? TerminalLaunch.TitleOf(Shell, folder ?? WorkingDirectory);
        }
    }

    /// <summary>The shell's exit code once it ended, else null.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>Whether the shell ended.</summary>
    public bool HasExited => ExitCode is not null;

    /// <summary>Completes with the shell's exit code.</summary>
    public Task<int> Exited => Session.Exited;

    /// <summary>Completes when the output has been read to its end and the exit is known (the tests wait on it).</summary>
    public Task Completion { get; }

    /// <summary>The output bytes read and parsed so far.</summary>
    public long BytesRead => Interlocked.Read(ref _bytesRead);

    /// <summary>The view's lines above the bottom (0 follows the output); kept on the same text while output arrives.</summary>
    public int ScrollBack
    {
        get => _scrollBack;
        set
        {
            lock (Screen.Sync)
            {
                _scrollBack = Math.Clamp(value, 0, Screen.ScrollbackLines);
                _scrolledSeen = Screen.LinesScrolled;
            }
        }
    }

    /// <summary>The selection, or null.</summary>
    public TerminalSelection? Selection { get; set; }

    /// <summary>Raised on the UI thread after output changed the screen (once per burst).</summary>
    public event EventHandler? Invalidated;

    /// <summary>Raised on the UI thread when the shell set its title or reported its folder.</summary>
    public event EventHandler? TitleChanged;

    /// <summary>Raised on the UI thread when the shell ended (<see cref="ExitCode"/> is set).</summary>
    public event EventHandler? ExitedChanged;

    /// <summary>Starts <paramref name="start"/>'s shell on <paramref name="host"/> with a screen keeping <paramref name="scrollback"/> lines.</summary>
    /// <exception cref="InvalidOperationException">The host is not available here or the shell did not start.</exception>
    public static TerminalInstance Start(int id, ITerminalHost host, TerminalStartInfo start, int scrollback, Action<Action> dispatch, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(dispatch);
        if (host.Unavailable is { } why)
        {
            throw new InvalidOperationException($"The terminal host '{host.Name}' cannot start a shell here: {why}");
        }

        var size = start.Size.Clamped();
        var screen = new TerminalScreen(size.Columns, size.Rows, scrollback);
        var session = host.Start(start with { Size = size });
        return new TerminalInstance(id, session, screen, start.Shell, start.WorkingDirectory, host.Name, name, dispatch);
    }

    /// <summary>Sends <paramref name="input"/> to the shell as it is (nothing once it ended).</summary>
    public void Write(ReadOnlySpan<byte> input)
    {
        if (Volatile.Read(ref _disposed) == 0 && !input.IsEmpty)
        {
            _input.Writer.TryWrite(input.ToArray());
        }
    }

    /// <summary>Typed text: to the shell as UTF-8; the view returns to the bottom.</summary>
    public void SendText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_suppressText >= 0)
        {
            // The character an Alt chord already sent as ESC+key, when the platform types it as well.
            bool same = text.Length == 1 && text[0] == _suppressText;
            _suppressText = -1;
            if (same)
            {
                return;
            }
        }

        if (text.Length == 0 || HasExited)
        {
            return;
        }

        Selection = null;
        ScrollBack = 0;
        Write(TerminalKeys.EncodeText(text));
    }

    /// <summary>A key: its VT bytes to the shell; false when it is not the terminal's (text, or a chord with no sequence).</summary>
    public bool HandleKey(EditorKey key, KeyModifiers modifiers)
    {
        _suppressText = -1;
        if (HasExited)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[TerminalKeys.MaxLength];
        int count = TerminalKeys.Encode(key, modifiers, Screen.ApplicationCursorKeys, bytes);
        if (count == 0)
        {
            return false;
        }

        if (count == 2 && bytes[0] == 0x1B && (modifiers & KeyModifiers.Alt) != 0 && bytes[1] is >= 0x20 and < 0x7F)
        {
            _suppressText = bytes[1];
        }

        Selection = null;
        ScrollBack = 0;
        Write(bytes[..count]);
        return true;
    }

    /// <summary>Pastes <paramref name="text"/> (line breaks as Enter, bracketed when the shell asked for it).</summary>
    public void Paste(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || HasExited)
        {
            return;
        }

        bool bracketed;
        lock (Screen.Sync)
        {
            bracketed = Screen.BracketedPaste;
        }

        ScrollBack = 0;
        Write(TerminalKeys.EncodePaste(text, bracketed));
    }

    /// <summary>The selected text, or null when nothing is selected.</summary>
    public string? SelectedText()
    {
        if (Selection is not { IsEmpty: false } selection)
        {
            return null;
        }

        lock (Screen.Sync)
        {
            return Screen.TextOf(selection.StartLine, selection.StartColumn, selection.EndLine, selection.EndColumn);
        }
    }

    /// <summary>Tells the screen and the shell the view's size in cells.</summary>
    public void Resize(TerminalSize size)
    {
        size = size.Clamped();
        bool changed;
        lock (Screen.Sync)
        {
            changed = size.Columns != Screen.Columns || size.Rows != Screen.Rows;
            if (changed)
            {
                Screen.Resize(size.Columns, size.Rows);
                _scrollBack = Math.Min(_scrollBack, Screen.ScrollbackLines);
            }
        }

        if (changed && !HasExited)
        {
            Session.Resize(size);
        }
    }

    /// <summary>Clears the scrollback and the screen but the cursor's row (the Clear command).</summary>
    public void Clear()
    {
        lock (Screen.Sync)
        {
            Screen.Clear();
            _scrollBack = 0;
            _scrolledSeen = Screen.LinesScrolled;
        }

        Selection = null;
        RequestInvalidate();
    }

    /// <summary>Ends the shell and frees the session (the tab may stay to show the end).</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _input.Writer.TryComplete();
        _stop.Cancel();
        Session.Dispose();
        _stop.Dispose();
    }

    // The thread pool's loop that sends the typed bytes to the shell, in order; a shell that ended takes nothing more.
    private async Task WriteAsync()
    {
        await foreach (var bytes in _input.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                continue;
            }

            try
            {
                Session.Write(bytes);
            }
            catch (Exception error) when (error is ObjectDisposedException or IOException or InvalidOperationException)
            {
                // The shell ended meanwhile: what it would have read goes nowhere, as a write after the end does.
            }
        }
    }

    private async Task ReadAsync()
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBytes);
        try
        {
            while (true)
            {
                int read;
                try
                {
                    read = await Session.ReadAsync(buffer.AsMemory(0, ReadBytes), _stop.Token).ConfigureAwait(false);
                }
                catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or IOException)
                {
                    break;
                }

                if (read == 0)
                {
                    break;
                }

                Feed(buffer.AsSpan(0, read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        int code;
        try
        {
            code = await Session.Exited.ConfigureAwait(false);
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException)
        {
            code = -1;
        }

        Feed(System.Text.Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"\r\n\e[0m\e[2m[the shell ended with exit code {code}]\e[0m\r\n")));
        _dispatch(() =>
        {
            ExitCode = code;
            ExitedChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>Parses <paramref name="data"/> in slices, each under the screen's lock, so a frame never waits long for it.</summary>
    private void Feed(ReadOnlySpan<byte> data)
    {
        Interlocked.Add(ref _bytesRead, data.Length);
        while (!data.IsEmpty)
        {
            var slice = data[..Math.Min(SliceBytes, data.Length)];
            lock (Screen.Sync)
            {
                Screen.Feed(slice);
            }

            data = data[slice.Length..];
        }

        RequestInvalidate();
    }

    private void RequestInvalidate()
    {
        if (Interlocked.Exchange(ref _invalidatePending, 1) != 0)
        {
            return;
        }

        _dispatch(() =>
        {
            Volatile.Write(ref _invalidatePending, 0);
            if (_scrollBack > 0)
            {
                // Scrolled back: stay on the same text while new lines push the screen up.
                lock (Screen.Sync)
                {
                    long scrolled = Screen.LinesScrolled;
                    _scrollBack = (int)Math.Min(Screen.ScrollbackLines, _scrollBack + (scrolled - _scrolledSeen));
                    _scrolledSeen = scrolled;
                }
            }

            Invalidated?.Invoke(this, EventArgs.Empty);
        });
    }
}
