// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using System.Runtime.InteropServices;
using System.Text;
using Craft.Abstractions;
using Craft.Editor.Terminal;
using Microsoft.Win32.SafeHandles;

namespace Craft.Maui.WinUI;

/// <summary>
/// "conpty": the integrated terminal's shell on the Windows pseudo-console (Windows 10 1809 and later). Two anonymous
/// pipes carry the input and the output, <c>CreatePseudoConsole</c> joins them to a console of the terminal's size, and
/// the shell is started with <c>PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE</c> so it (and everything it runs) writes VT text to
/// the output pipe and reads the keys as VT from the input pipe. The default shell: pwsh when on the PATH, else Windows
/// PowerShell, else cmd (<c>%ComSpec%</c>). Closing the pseudo-console ends the processes attached to it.
/// </summary>
public sealed partial class ConPtyTerminalHost : ITerminalHost
{
    private const uint ExtendedStartupInfoPresent = 0x0008_0000;
    private const uint CreateUnicodeEnvironment = 0x0000_0400;
    private const int StartfUseStdHandles = 0x0000_0100;
    private static readonly nint ProcThreadAttributePseudoConsole = 0x0002_0016;

    /// <summary>The host's name.</summary>
    public const string HostName = "conpty";

    /// <inheritdoc />
    public string Name => HostName;

    /// <inheritdoc />
    public string Summary => "the Windows pseudo-console (ConPTY): the shell sees a real console of the terminal's size";

    /// <inheritdoc />
    public string? Unavailable => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763) ? null : "the Windows pseudo-console needs Windows 10 version 1809 or later";

    /// <inheritdoc />
    public TerminalShell DefaultShell()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        var extensions = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
        if (TerminalLaunch.FindOnPath("pwsh", path, ';', extensions) is { } pwsh)
        {
            return new TerminalShell(pwsh, "-NoLogo");
        }

        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var windowsPowerShell = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(windowsPowerShell))
        {
            return new TerminalShell(windowsPowerShell, "-NoLogo");
        }

        return new TerminalShell(Environment.GetEnvironmentVariable("ComSpec") is { Length: > 0 } comspec ? comspec : Path.Combine(system, "cmd.exe"));
    }

    /// <inheritdoc />
    public ITerminalSession Start(TerminalStartInfo start)
    {
        ArgumentNullException.ThrowIfNull(start);
        var program = start.Shell.Program;
        if (!Path.IsPathRooted(program))
        {
            program = TerminalLaunch.FindOnPath(program, start.Environment.GetValueOrDefault("PATH") ?? Environment.GetEnvironmentVariable("PATH"), ';',
                start.Environment.GetValueOrDefault("PATHEXT") ?? Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                ?? throw new InvalidOperationException($"'{start.Shell.Program}' is not on the PATH (CRAFT_TERMINAL_SHELL names the shell).");
        }

        var size = start.Size.Clamped();
        if (!CreatePipe(out var inputRead, out var inputWrite, 0, 0))
        {
            throw Failure("CreatePipe (input)");
        }

        if (!CreatePipe(out var outputRead, out var outputWrite, 0, 0))
        {
            int error = Marshal.GetLastPInvokeError();
            inputRead.Dispose();
            inputWrite.Dispose();
            throw Failure("CreatePipe (output)", error);
        }

        nint console = 0;
        nint attributes = 0;
        bool started = false;
        try
        {
            int result = CreatePseudoConsole(new Coord((short)size.Columns, (short)size.Rows), inputRead, outputWrite, 0, out console);
            if (result != 0)
            {
                throw new InvalidOperationException($"CreatePseudoConsole failed (HRESULT 0x{result:X8}).");
            }

            nint length = 0;
            _ = InitializeProcThreadAttributeList(0, 1, 0, ref length);
            attributes = Marshal.AllocHGlobal(length);
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref length))
            {
                throw Failure("InitializeProcThreadAttributeList");
            }

            if (!UpdateProcThreadAttribute(attributes, 0, ProcThreadAttributePseudoConsole, console, IntPtr.Size, 0, 0))
            {
                throw Failure("UpdateProcThreadAttribute");
            }

            var info = new StartupInfoEx
            {
                Cb = Marshal.SizeOf<StartupInfoEx>(),
                // Standard handles of none: the child takes the pseudo-console's, never a handle this process holds.
                Flags = StartfUseStdHandles,
                AttributeList = attributes,
            };
            var commandLine = CommandLine(program, start.Shell.Arguments);
            var environment = EnvironmentBlock(start.Environment);
            ProcessInformation process;
            unsafe
            {
                fixed (char* line = commandLine)
                fixed (char* block = environment)
                {
                    if (!CreateProcess(null, line, 0, 0, false, ExtendedStartupInfoPresent | CreateUnicodeEnvironment, block, start.WorkingDirectory, ref info, out process))
                    {
                        throw Failure($"CreateProcess of '{program}' in '{start.WorkingDirectory}'");
                    }
                }
            }

            _ = CloseHandle(process.Thread);
            var session = new ConPtySession(console, inputWrite, outputRead, new SafeWaitHandle(process.Process, ownsHandle: true), process.ProcessId);
            started = true;
            return session;
        }
        finally
        {
            if (attributes != 0)
            {
                DeleteProcThreadAttributeList(attributes);
                Marshal.FreeHGlobal(attributes);
            }

            // The pseudo-console holds its own copies of its ends of the pipes.
            inputRead.Dispose();
            outputWrite.Dispose();
            if (!started)
            {
                if (console != 0)
                {
                    ClosePseudoConsole(console);
                }

                inputWrite.Dispose();
                outputRead.Dispose();
            }
        }
    }

    /// <summary>The command line: the program quoted when it has a space, then the arguments as written.</summary>
    internal static char[] CommandLine(string program, string arguments)
    {
        var line = new StringBuilder(program.Length + arguments.Length + 4);
        bool quote = program.Contains(' ', StringComparison.Ordinal) && !program.StartsWith('"');
        line.Append(quote ? "\"" + program + "\"" : program);
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            line.Append(' ').Append(arguments);
        }

        line.Append('\0');
        var chars = new char[line.Length];
        line.CopyTo(0, chars, chars.Length);
        return chars;
    }

    /// <summary>A Unicode environment block: "NAME=value\0" for each variable, sorted by name, then a final "\0".</summary>
    internal static char[] EnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var block = new StringBuilder();
        foreach (var name in environment.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (name.Length > 0 && !name.Contains('\0', StringComparison.Ordinal))
            {
                block.Append(name).Append('=').Append(environment[name]).Append('\0');
            }
        }

        block.Append('\0');
        var chars = new char[block.Length];
        block.CopyTo(0, chars, chars.Length);
        return chars;
    }

    private static InvalidOperationException Failure(string what, int? error = null)
    {
        int code = error ?? Marshal.GetLastPInvokeError();
        return new InvalidOperationException($"{what} failed: {Marshal.GetPInvokeErrorMessage(code)} (error {code}).");
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, nint attributes, int size);

    [LibraryImport("kernel32.dll")]
    private static partial int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out nint console);

    [LibraryImport("kernel32.dll")]
    private static partial int ResizePseudoConsole(nint console, Coord size);

    [LibraryImport("kernel32.dll")]
    private static partial void ClosePseudoConsole(nint console);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(nint list, uint flags, nint attribute, nint value, nint size, nint previousValue, nint returnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(nint list);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CreateProcess(string? applicationName, char* commandLine, nint processAttributes, nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags, char* environment, string? currentDirectory, ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(SafeWaitHandle process, out uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeWaitHandle process, uint exitCode);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Coord(short x, short y)
    {
        public readonly short X = x;
        public readonly short Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public int Cb;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public nint Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
        public nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    /// <summary>A shell on a pseudo-console: the output pipe read by one reader, the input pipe written under a lock.</summary>
    private sealed class ConPtySession : ITerminalSession
    {
        private readonly Lock _lock = new();
        private readonly FileStream _input;
        private readonly FileStream _output;
        private readonly SafeWaitHandle _process;
        private readonly ProcessWait _processWait;
        private readonly RegisteredWaitHandle _wait;
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private nint _console;
        private bool _disposed;

        public ConPtySession(nint console, SafeFileHandle input, SafeFileHandle output, SafeWaitHandle process, int processId)
        {
            _console = console;
            _input = new FileStream(input, FileAccess.Write, 0);
            _output = new FileStream(output, FileAccess.Read, 0);
            _process = process;
            ProcessId = processId;
            _processWait = new ProcessWait(process);
            _wait = ThreadPool.RegisterWaitForSingleObject(_processWait, static (state, _) => ((ConPtySession)state!).OnExited(), this, Timeout.Infinite, executeOnlyOnce: true);
        }

        public int ProcessId { get; }

        public Task<int> Exited => _exited.Task;

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                return await _output.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // The pseudo-console closed its end (the shell ended): the output is over.
                return 0;
            }
            catch (ObjectDisposedException)
            {
                return 0;
            }
        }

        public void Write(ReadOnlySpan<byte> input)
        {
            lock (_lock)
            {
                if (_disposed || _console == 0)
                {
                    return;
                }

                try
                {
                    _input.Write(input);
                    _input.Flush();
                }
                catch (IOException)
                {
                    // The shell is ending: what is typed now goes nowhere.
                }
            }
        }

        public void Resize(TerminalSize size)
        {
            size = size.Clamped();
            lock (_lock)
            {
                if (!_disposed && _console != 0)
                {
                    _ = ResizePseudoConsole(_console, new Coord((short)size.Columns, (short)size.Rows));
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            _wait.Unregister(null);
            if (!_exited.Task.IsCompleted)
            {
                _ = TerminateProcess(_process, 1);
            }

            CloseConsole();
            _input.Dispose();
            _output.Dispose();
            _exited.TrySetResult(-1);
            _processWait.Dispose();
            _process.Dispose();
        }

        private void OnExited()
        {
            int code = GetExitCodeProcess(_process, out uint exit) ? unchecked((int)exit) : -1;
            _exited.TrySetResult(code);

            // Closing the pseudo-console lets its output end (the reader is still reading what is left, so it cannot block for good).
            CloseConsole();
        }

        private void CloseConsole()
        {
            nint console;
            lock (_lock)
            {
                console = _console;
                _console = 0;
            }

            if (console != 0)
            {
                ClosePseudoConsole(console);
            }
        }
    }

    /// <summary>The process handle as a wait handle (the thread pool waits on it; the session owns the handle).</summary>
    private sealed class ProcessWait : WaitHandle
    {
        public ProcessWait(SafeWaitHandle process) => SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false);
    }
}
#endif
