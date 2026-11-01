// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.IO.Pipes;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Editor;
using Craft.Editor.Terminal;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// X part 6b: a program's terminal tab (<see cref="IEditorTerminals.OpenProgram"/>): its output drawn as a shell's, what is
/// typed sent to its input through a terminal's line discipline (echo, Backspace, Ctrl+U, Enter, Ctrl+D, Ctrl+C), its end
/// the plug-in's; the manager replacing an ended tab of the same name, giving it the keyboard, stopping a running program
/// whose tab is closed.
/// </summary>
[TestGroup("Terminal")]
public static class ProgramTerminalTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    [Test]
    public static async Task AProgramTabEchoesAndEditsTheLineSendsItAndEndsItsInput()
    {
        var input = new CapturingStream();
        using var output = new MemoryStream("first\nsecond\r\n\e[31mred\e[0m"u8.ToArray());
        int stops = 0;
        var session = new ProgramTerminalSession(new EditorProgramRequest("Run: x", input, output) { Header = "$ x\n", Stop = () => Interlocked.Increment(ref stops) });
        await session.OutputEnded.WaitAsync(Wait);

        session.Write("ab"u8);
        session.Write([0x7F]);                                         // Backspace: the b back
        session.Write("é"u8);
        session.Write([0x7F]);                                         // a two-byte character back whole
        session.Write("c\r"u8);                                        // Enter
        Check.Equal("ac\n", input.Text, "Enter sends the edited line with its line feed");
        session.Write("zz"u8);
        session.Write([0x15]);                                         // Ctrl+U: the whole line
        session.Write("\e[Aq\eOD"u8);                                  // an arrow's sequences are dropped
        session.Write([0x04]);                                         // Ctrl+D inside a line: the line without its end
        Check.Equal("ac\nq", input.Text, "Ctrl+U clears the line, key sequences are dropped, Ctrl+D sends a started line as it is");
        Check.True(!input.Closed, "the input stays open");
        session.Write([0x04]);                                         // Ctrl+D at a line's start: the input ends
        Check.True(input.Closed, "Ctrl+D at a line's start closes the program's input");
        session.Write("more\r"u8);
        Check.Equal("ac\nq", input.Text, "nothing goes to a closed input");
        session.Write([0x03]);
        Check.Equal(1, Volatile.Read(ref stops), "Ctrl+C asks the caller to stop the program (a pipe carries no signal), also after the input closed");

        Check.True(session.Append("[a note]\n"), "the plug-in adds a note");
        session.End("[exit code 3]\n", 3);
        Check.True(!session.IsOpen && !session.Append("late") && await session.Exited.WaitAsync(Wait) == 3, "ended: closed, its exit code reported");

        var all = new MemoryStream();
        var buffer = new byte[7];                                      // small reads: a chunk read in parts
        int read;
        while ((read = await session.ReadAsync(buffer)) > 0)
        {
            await all.WriteAsync(buffer.AsMemory(0, read));
        }

        string text = Encoding.UTF8.GetString(all.ToArray());
        Check.True(text.StartsWith("$ x\r\nfirst\r\nsecond\r\n\e[31mred\e[0m", StringComparison.Ordinal),
            $"the header first, then the output with each lone line feed drawn as CR LF and its colours kept: {Show(text)}");
        Check.True(text.Contains("ab\b \bé\b \bc\r\n", StringComparison.Ordinal) && text.Contains("^C\r\n", StringComparison.Ordinal), $"what is typed is echoed and edited in the tab: {Show(text)}");
        Check.True(text.EndsWith("[a note]\r\n[exit code 3]\r\n", StringComparison.Ordinal), $"the note and the end last: {Show(text)}");
    }

    [Test]
    public static async Task TheManagerReplacesAnEndedTabGivesItTheKeyboardAndStopsAProgramWhoseTabCloses()
    {
        var options = new CraftOptions();
        using var host = new EditorHost(options);
        using var manager = new TerminalManager(host, () => options, new FakeClipboard());
        host.Terminals = manager;
        var requests = new List<TerminalViewRequest>();
        manager.ViewRequested += (_, request) => requests.Add(request);
        var earlier = manager.OpenOutput("Run: x");
        manager.Open((string?)null);                                   // a shell after it: the replaced tab keeps its place
        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out);
        using var reading = new AnonymousPipeClientStream(PipeDirection.In, pipe.ClientSafePipeHandle);
        var input = new CapturingStream();
        int stops = 0;
        IEditorHost plugin = host;
        var tab = plugin.Terminals!.OpenProgram(new EditorProgramRequest("Run: x", input, reading) { Stop = () => Interlocked.Increment(ref stops) })!;
        var program = manager.Terminals[0];
        Check.True(program is { IsProgram: true, IsReadOnly: false, Title: "Run: x" } && manager.Terminals.Count == 2 && !earlier.IsOpen,
            "the output tab of the same name is replaced in its place by the program's tab");
        Check.True(ReferenceEquals(manager.ActiveTerminal, program), "the program's tab is the active one");
        tab.Show();
        Check.True(requests is [TerminalViewRequest.Show], "Show gives the tab the keyboard");

        await pipe.WriteAsync("Name? "u8.ToArray());
        await pipe.FlushAsync();
        string prompt = "";
        for (var until = DateTime.UtcNow + Wait; DateTime.UtcNow < until && !prompt.Contains("Name?", StringComparison.Ordinal);)
        {
            await Task.Delay(10);
            prompt = Text(program);
        }

        Check.True(prompt.Contains("Name?", StringComparison.Ordinal), $"the program's prompt reaches the screen before anything is typed ({prompt.Trim()})");
        manager.Input.OnText("Ada");
        Check.True(manager.Input.OnKey(EditorKey.Enter, KeyModifiers.None), "Enter goes to the program's tab");
        for (int i = 0; i < 300 && input.Text != "Ada\n"; i++)
        {
            await Task.Delay(10);
        }

        Check.Equal("Ada\n", input.Text, "what is typed through the terminal view reaches the program's input as a line");
        string screen = "";
        for (int i = 0; i < 300 && !screen.Contains("Name? Ada", StringComparison.Ordinal); i++)
        {
            await Task.Delay(10);
            screen = Text(program);
        }

        Check.True(screen.Contains("Name? Ada", StringComparison.Ordinal), $"the echo follows the program's prompt on its line ({screen.Trim()})");
        manager.Input.OnKey(EditorKey.C, KeyModifiers.Control);
        for (int i = 0; i < 300 && Volatile.Read(ref stops) == 0; i++)
        {
            await Task.Delay(10);
        }

        Check.Equal(1, Volatile.Read(ref stops), "Ctrl+C with nothing selected stops the program");
        manager.Close(program);
        Check.Equal(2, Volatile.Read(ref stops), "closing the tab of a program still running stops it");
        Check.True(!tab.IsOpen && !tab.Append("late"), "the closed tab's handle takes no more");

        // An ended program's tab is replaced by the next run of the same name; a running one is not.
        using var ended = new MemoryStream("done\n"u8.ToArray());
        var first = manager.OpenProgram(new EditorProgramRequest("Run: y", new CapturingStream(), ended));
        first.End("[exit code 0]\n", 0);
        for (int i = 0; i < 300 && !manager.Terminals.Any(t => t.Title == "Run: y" && t.HasExited); i++)
        {
            await Task.Delay(10);
        }

        using var again = new MemoryStream("again\n"u8.ToArray());
        manager.OpenProgram(new EditorProgramRequest("Run: y", new CapturingStream(), again));
        Check.Equal(1, manager.Terminals.Count(t => t.Title == "Run: y"), "the ended tab of the same name is replaced");
    }

    private static string Text(TerminalInstance terminal)
    {
        lock (terminal.Screen.Sync)
        {
            return terminal.Screen.TextOf(terminal.Screen.FirstLine, 0, terminal.Screen.ScreenTop + terminal.Screen.Rows - 1, terminal.Screen.Columns);
        }
    }

    private static string Show(string text) => text.Replace("\e", "\\e", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\b", "\\b", StringComparison.Ordinal);

    /// <summary>A program's input as the tests see it: what was written, and whether it was closed.</summary>
    internal sealed class CapturingStream : Stream
    {
        private readonly MemoryStream _written = new();

        public string Text
        {
            get
            {
                lock (_written)
                {
                    return Encoding.UTF8.GetString(_written.ToArray());
                }
            }
        }

        public bool Closed { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => !Closed;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(Closed, this);
            lock (_written)
            {
                _written.Write(buffer);
            }
        }

        protected override void Dispose(bool disposing)
        {
            Closed = true;
            base.Dispose(disposing);
        }
    }
}
