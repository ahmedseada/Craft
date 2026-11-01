// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;

namespace Craft.Abstractions;

/// <summary>A terminal's size in character cells.</summary>
/// <param name="Columns">The cells of a row (at least 1).</param>
/// <param name="Rows">The rows (at least 1).</param>
public readonly record struct TerminalSize(int Columns, int Rows)
{
    /// <summary>The size a terminal starts with before its view measured it (120 by 30).</summary>
    public static TerminalSize Default => new(120, 30);

    /// <summary>This size with both sides at least 1 and at most 10,000 (what a pseudo-console takes).</summary>
    public TerminalSize Clamped() => new(Math.Clamp(Columns, 1, 10_000), Math.Clamp(Rows, 1, 10_000));
}

/// <summary>A shell to start: its program and its arguments as one command-line text.</summary>
/// <param name="Program">The program (a full path, or a name the host looks up on the PATH).</param>
/// <param name="Arguments">Its arguments as the platform's command line writes them, or empty.</param>
public sealed record TerminalShell(string Program, string Arguments = "");

/// <summary>What a terminal session starts with.</summary>
/// <param name="Shell">The program and its arguments.</param>
/// <param name="WorkingDirectory">The folder it starts in (a full path that exists).</param>
/// <param name="Size">Its first size (the view resizes it once it measured itself).</param>
/// <param name="Environment">Its whole environment (the app's own with the user's additions, <c>CRAFT_TERMINAL_ENV</c>).</param>
public sealed record TerminalStartInfo(TerminalShell Shell, string WorkingDirectory, TerminalSize Size, IReadOnlyDictionary<string, string> Environment);

/// <summary>
/// A running shell behind a terminal (<see cref="ITerminalHost.Start"/>): its output is a byte stream (UTF-8 and VT
/// sequences, read with <see cref="ReadAsync"/> by one reader), its input bytes are written as typed, it is resized in
/// cells, and it reports its exit code. Disposing it ends the shell (and what it runs, where the host can) and frees its
/// handles. <see cref="Write"/>, <see cref="Resize"/> and <c>Dispose</c> may be called from any thread.
/// </summary>
public interface ITerminalSession : IDisposable
{
    /// <summary>The shell's process id.</summary>
    int ProcessId { get; }

    /// <summary>
    /// Reads the next output bytes into <paramref name="buffer"/>: the count read, or 0 once the output has ended (the shell
    /// exited and everything it wrote was read). One reader at a time.
    /// </summary>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>Sends <paramref name="input"/> (UTF-8 text and VT key sequences) to the shell; nothing once it ended.</summary>
    void Write(ReadOnlySpan<byte> input);

    /// <summary>Tells the shell its new size in cells.</summary>
    void Resize(TerminalSize size);

    /// <summary>Completes with the shell's exit code when it ends (-1 when it was killed and no code was known).</summary>
    Task<int> Exited { get; }
}

/// <summary>
/// Starts shells for the integrated terminal, registered in <see cref="TerminalHosts"/>: the library's "pipe" (a plain
/// process with redirected pipes, on any platform, no pseudo-console: a shell there runs without line editing; Enter's CR reaches it as a line feed), the
/// Windows app's "conpty" (the Windows pseudo-console, under <c>Platforms/Windows</c>), later a phone's (or none).
/// </summary>
public interface ITerminalHost : IKind
{
    /// <summary>Why this host cannot start a shell here, or null when it can (a platform with no shell says so).</summary>
    string? Unavailable => null;

    /// <summary>The shell started when the user names none (<c>CRAFT_TERMINAL_SHELL</c> empty), with its arguments.</summary>
    TerminalShell DefaultShell();

    /// <summary>Starts <paramref name="start"/>'s shell.</summary>
    /// <exception cref="InvalidOperationException">The shell could not be started (the message names the program and why).</exception>
    ITerminalSession Start(TerminalStartInfo start);
}

/// <summary>
/// The terminal hosts, by name (ignoring case). The library's: <see cref="Pipe"/>. The Windows app registers "conpty". An
/// unknown name fails with "not registered"; <c>CRAFT_TERMINAL_HOST</c> chooses one ("auto": the platform's, else "pipe").
/// </summary>
public static class TerminalHosts
{
    /// <summary>The library's host: a plain process with its standard input, output and error redirected.</summary>
    public const string Pipe = "pipe";

    /// <summary>The setting's value that takes the platform's host when one is registered, else <see cref="Pipe"/>.</summary>
    public const string Auto = "auto";

    private static readonly KindTable<ITerminalHost> Table = BuiltIn();

    private static KindTable<ITerminalHost> BuiltIn()
    {
        var table = new KindTable<ITerminalHost>(nameof(TerminalHosts), "Terminal host", "add it with TerminalHosts.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a host is asked once per terminal opened");
        table.RegisterDefault(new PipeTerminalHost());
        return table;
    }

    /// <summary>Registers <paramref name="host"/> under its name (over the library's of that name, which stays behind it).</summary>
    public static void Register(ITerminalHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        Table.Register(host, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's host of that name (the library's, if any, answers again).</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered hosts, in order.</summary>
    public static IReadOnlyList<ITerminalHost> All => Table.Kinds;

    /// <summary>The host of that name, or null.</summary>
    public static ITerminalHost? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The host of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static ITerminalHost Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>
    /// The host <paramref name="name"/> names: <see cref="Auto"/> (or empty) gives the last registered host other than
    /// <see cref="Pipe"/> that is available here, else <see cref="Pipe"/>; another name must be registered.
    /// </summary>
    /// <exception cref="NotRegisteredException">No host is registered under <paramref name="name"/>.</exception>
    public static ITerminalHost Resolve(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name) && !name.Trim().Equals(Auto, StringComparison.OrdinalIgnoreCase))
        {
            return Get(name.Trim());
        }

        var hosts = Table.Kinds;
        for (int i = hosts.Count - 1; i >= 0; i--)
        {
            if (!hosts[i].Name.Equals(Pipe, StringComparison.OrdinalIgnoreCase) && hosts[i].Unavailable is null)
            {
                return hosts[i];
            }
        }

        return Get(Pipe);
    }
}

/// <summary>A terminal a plug-in asks the host to open (<see cref="IEditorTerminals.Open"/>).</summary>
public sealed record EditorTerminalRequest
{
    /// <summary>The folder it starts in; null follows the user's rule (the open folder, else the shell's home).</summary>
    public string? Folder { get; init; }

    /// <summary>The tab's name, or null for the shell's and its folder's.</summary>
    public string? Name { get; init; }

    /// <summary>The program, or null for the user's shell (<c>CRAFT_TERMINAL_SHELL</c>).</summary>
    public TerminalShell? Shell { get; init; }

    /// <summary>Variables added to (or, with a null value, removed from) the terminal's environment.</summary>
    public IReadOnlyDictionary<string, string?>? Environment { get; init; }

    /// <summary>Whether the Terminal tab shows it at once.</summary>
    public bool Show { get; init; } = true;
}

/// <summary>
/// A terminal as a plug-in holds it: it can write to it (as if typed) and show it, never read what the user types. The
/// user's terminal stays the user's: a plug-in's handle stops working when the plug-in is deactivated.
/// </summary>
public interface IEditorTerminal
{
    /// <summary>Its tab's title (the shell's own title when it set one).</summary>
    string Title { get; }

    /// <summary>The folder it started in.</summary>
    string WorkingDirectory { get; }

    /// <summary>Completes with the shell's exit code when it ends.</summary>
    Task<int> Exited { get; }

    /// <summary>Sends <paramref name="text"/> to the shell as typed (a line ends with "\r" to run it); false once it ended or the handle closed.</summary>
    bool Write(string text);

    /// <summary>Shows it in the Terminal tab.</summary>
    void Show();
}

/// <summary>The integrated terminals, as plug-ins reach them (<see cref="IEditorHost.Terminals"/>). Called on the UI thread.</summary>
public interface IEditorTerminals
{
    /// <summary>The terminals open now, in their tabs' order.</summary>
    IReadOnlyList<IEditorTerminal> All { get; }

    /// <summary>The terminal shown in the Terminal tab, or null.</summary>
    IEditorTerminal? Active { get; }

    /// <summary>Opens a terminal.</summary>
    /// <exception cref="InvalidOperationException">No shell could be started (the message says why).</exception>
    IEditorTerminal Open(EditorTerminalRequest request);

    /// <summary>
    /// The read-only terminal tab named <paramref name="name"/> ("Agent sandbox"), opened when it is not open: no shell
    /// runs in it and nothing the user types reaches anything; the plug-in appends text (VT colours included) and the
    /// Terminal tab shows it with its scrollback, selection and copy. One tab per name; null when the host has none.
    /// </summary>
    IEditorTerminalOutput? OpenOutput(string name) => null;

    /// <summary>
    /// A terminal tab over a program the plug-in started itself (X part 6b: a run that reads input; its standard streams,
    /// such as Idrak's <c>ICommandSession</c>'s): its output and error are drawn as a shell's (colours, the VT parser), and
    /// what the user types goes to its input as a terminal's line discipline gives it to a program reading a pipe (the
    /// line echoed and edited in the tab, Enter sending it, Ctrl+D at a line's start closing the input). Ctrl+C with nothing
    /// selected, and closing the tab while it runs, call <see cref="EditorProgramRequest.Stop"/> (a pipe carries no signal).
    /// A tab of the same name that ended or is an output tab is replaced in its place. Null when the host has no terminals.
    /// </summary>
    IEditorTerminalProgram? OpenProgram(EditorProgramRequest request) => null;
}

/// <summary>A program's tab to open (<see cref="IEditorTerminals.OpenProgram"/>).</summary>
/// <param name="Name">The tab's name ("Run: App").</param>
/// <param name="Input">The program's standard input: what the user types is written to it (and flushed).</param>
/// <param name="Output">The program's standard output, read to its end.</param>
public sealed record EditorProgramRequest(string Name, Stream Input, Stream Output)
{
    /// <summary>The program's standard error, read to its end beside <see cref="Output"/>; null: none.</summary>
    public Stream? Error { get; init; }

    /// <summary>What Ctrl+C (with nothing selected) and closing the tab do while the program runs: the caller stops it.</summary>
    public Action? Stop { get; init; }

    /// <summary>The folder it runs in (the tab's tooltip), or null.</summary>
    public string? Folder { get; init; }

    /// <summary>Text shown before the program's output (the command line; VT colours allowed, "\n" a line break), or null.</summary>
    public string? Header { get; init; }
}

/// <summary>
/// A program's terminal tab (<see cref="IEditorTerminals.OpenProgram"/>). <see cref="IEditorTerminalOutput.Append"/> adds a
/// note of the plug-in's between the program's output; <see cref="IEditorTerminalOutput.Show"/> shows the tab and gives it
/// the keyboard. May be called from any thread.
/// </summary>
public interface IEditorTerminalProgram : IEditorTerminalOutput
{
    /// <summary>Completes once the program's output and error were read to their end (it closed them, as it does when it exits).</summary>
    Task OutputEnded { get; }

    /// <summary>
    /// Ends the tab: <paramref name="text"/> (the run's end, its exit code) is shown last, nothing typed goes anywhere after
    /// it, and the tab shows as ended with <paramref name="exitCode"/>; it stays open to be read.
    /// </summary>
    void End(string? text = null, int exitCode = 0);
}

/// <summary>
/// A read-only terminal tab a plug-in writes to (<see cref="IEditorTerminals.OpenOutput"/>): its output, never a shell.
/// <see cref="Append"/> may be called from any thread. The handle stops working once the user closed the tab (ask
/// <see cref="IEditorTerminals.OpenOutput"/> again for a new one) or the plug-in was deactivated.
/// </summary>
public interface IEditorTerminalOutput
{
    /// <summary>Its tab's title.</summary>
    string Title { get; }

    /// <summary>Whether the tab is still open (false once the user closed it).</summary>
    bool IsOpen { get; }

    /// <summary>Appends <paramref name="text"/> (a line break is "\n"; VT sequences colour it); false once the tab closed.</summary>
    bool Append(string text);

    /// <summary>Shows the tab in the bottom panel's Terminal tab, without taking the keyboard.</summary>
    void Show();
}

/// <summary>
/// "pipe": a plain process with its standard input, output and error redirected (System.Diagnostics.Process), on every
/// platform. There is no pseudo-console, so a shell sees pipes, not a terminal: it prints no prompt of its own and edits
/// no lines, but it runs what it is sent and its output, colours included, reaches the screen. The tests drive a session
/// through it headless; on Windows the app registers "conpty" over it.
/// </summary>
internal sealed class PipeTerminalHost : ITerminalHost
{
    public string Name => TerminalHosts.Pipe;

    public string Summary => "a plain process with redirected standard input, output and error (no pseudo-console)";

    public TerminalShell DefaultShell()
    {
        if (OperatingSystem.IsWindows())
        {
            return new TerminalShell(System.Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comspec ? comspec : "cmd.exe");
        }

        return new TerminalShell(System.Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } shell ? shell : "/bin/sh");
    }

    public ITerminalSession Start(TerminalStartInfo start)
    {
        ArgumentNullException.ThrowIfNull(start);
        var info = new ProcessStartInfo(start.Shell.Program, start.Shell.Arguments)
        {
            WorkingDirectory = start.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        info.Environment.Clear();
        foreach (var (name, value) in start.Environment)
        {
            info.Environment[name] = value;
        }

        info.Environment["COLUMNS"] = start.Size.Columns.ToString(System.Globalization.CultureInfo.InvariantCulture);
        info.Environment["LINES"] = start.Size.Rows.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Process process;
        try
        {
            process = Process.Start(info) ?? throw new InvalidOperationException($"'{start.Shell.Program}' did not start.");
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new InvalidOperationException($"'{start.Shell.Program}' could not be started in '{start.WorkingDirectory}': {error.Message}", error);
        }

        return new PipeTerminalSession(process);
    }

    /// <summary>A process's two output streams merged into one byte stream, in the order the chunks arrive.</summary>
    private sealed class PipeTerminalSession : ITerminalSession
    {
        private const int ChunkBytes = 16 * 1024;
        private readonly Process _process;
        private readonly Stream _input;
        private readonly Channel<(byte[] Chunk, int Count)> _output = Channel.CreateBounded<(byte[], int)>(new BoundedChannelOptions(64) { SingleReader = true });
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock _writeLock = new();
        private byte[]? _pending;
        private int _pendingStart;
        private int _pendingCount;
        private int _disposed;

        public PipeTerminalSession(Process process)
        {
            _process = process;
            _input = process.StandardInput.BaseStream;
            ProcessId = process.Id;
            var stdout = Pump(process.StandardOutput.BaseStream);
            var stderr = Pump(process.StandardError.BaseStream);
            _ = Task.WhenAll(stdout, stderr).ContinueWith(async _ =>
            {
                _output.Writer.TryComplete();
                await process.WaitForExitAsync().ConfigureAwait(false);
                _exited.TrySetResult(SafeExitCode(process));
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
        }

        public int ProcessId { get; }

        public Task<int> Exited => _exited.Task;

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
                        ArrayPool<byte>.Shared.Return(pending);
                    }

                    return count;
                }

                if (!await _output.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return 0;
                }

                if (_output.Reader.TryRead(out var next))
                {
                    _pending = next.Chunk;
                    _pendingStart = 0;
                    _pendingCount = next.Count;
                }
            }
        }

        public void Write(ReadOnlySpan<byte> input)
        {
            if (Volatile.Read(ref _disposed) != 0 || _exited.Task.IsCompleted)
            {
                return;
            }

            lock (_writeLock)
            {
                try
                {
                    WriteLines(input);
                    _input.Flush();
                }
                catch (IOException)
                {
                    // The shell closed its input (it is ending): what is typed now goes nowhere.
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        /// <summary>Enter (CR) as a line feed, as a terminal's line discipline gives a shell (ICRNL); a CR LF pair as one.</summary>
        private void WriteLines(ReadOnlySpan<byte> input)
        {
            if (input.IndexOf((byte)'\r') < 0)
            {
                _input.Write(input);
                return;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(input.Length);
            try
            {
                int length = 0;
                for (int i = 0; i < input.Length; i++)
                {
                    byte b = input[i];
                    if (b == (byte)'\n' && i > 0 && input[i - 1] == (byte)'\r')
                    {
                        continue;
                    }

                    buffer[length++] = b == (byte)'\r' ? (byte)'\n' : b;
                }

                _input.Write(buffer, 0, length);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void Resize(TerminalSize size)
        {
            // A pipe has no size: the shell read COLUMNS and LINES at its start.
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // It ended meanwhile.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not ours to end any more (it ended, or a child outlived it): nothing is left to free here.
            }

            _exited.TrySetResult(SafeExitCode(_process));
            _process.Dispose();
        }

        private static int SafeExitCode(Process process)
        {
            try
            {
                return process.HasExited ? process.ExitCode : -1;
            }
            catch (InvalidOperationException)
            {
                return -1;
            }
        }

        /// <summary>
        /// A lone LF as CR LF, as a terminal's line discipline gives the screen (ONLCR): a program writing to a pipe ends its
        /// lines with LF only. The chunk is replaced by a larger rented one when it has any.
        /// </summary>
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

        private async Task Pump(Stream stream)
        {
            bool lastWasReturn = false;
            try
            {
                while (true)
                {
                    var chunk = ArrayPool<byte>.Shared.Rent(ChunkBytes);
                    int read;
                    try
                    {
                        read = await stream.ReadAsync(chunk.AsMemory(0, ChunkBytes)).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is IOException or ObjectDisposedException)
                    {
                        read = 0;
                    }

                    if (read > 0)
                    {
                        (chunk, read) = LineFeedsAsNewLines(chunk, read, ref lastWasReturn);
                    }

                    if (read == 0 || !await _output.Writer.WaitToWriteAsync().ConfigureAwait(false) || !_output.Writer.TryWrite((chunk, read)))
                    {
                        ArrayPool<byte>.Shared.Return(chunk);
                        return;
                    }
                }
            }
            catch (ChannelClosedException)
            {
            }
        }
    }
}
