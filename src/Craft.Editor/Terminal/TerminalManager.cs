// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections;
using Craft.Abstractions;
using Craft.Core;
using Craft.Editor.Abstractions;

namespace Craft.Editor.Terminal;

/// <summary>The terminal's command ids (registered in <see cref="EditorCommands"/> by <see cref="TerminalManager"/>).</summary>
public static class TerminalCommandIds
{
    /// <summary>A new terminal in the start folder (Ctrl+Shift+`).</summary>
    public const string New = "terminal.new";

    /// <summary>Shows the Terminal tab (a new terminal when there is none), or hides the panel when it shows (Ctrl+`).</summary>
    public const string Toggle = "terminal.toggle";

    /// <summary>The next terminal's tab.</summary>
    public const string Next = "terminal.next";

    /// <summary>The previous terminal's tab.</summary>
    public const string Previous = "terminal.previous";

    /// <summary>Ends the active terminal's shell and closes its tab.</summary>
    public const string Kill = "terminal.kill";

    /// <summary>Clears the active terminal (its scrollback and screen, the prompt's row kept).</summary>
    public const string Clear = "terminal.clear";

    /// <summary>Copies the terminal's selection (Ctrl+Shift+C).</summary>
    public const string Copy = "terminal.copy";

    /// <summary>Pastes into the terminal (Ctrl+Shift+V).</summary>
    public const string Paste = "terminal.paste";

    /// <summary>Sends the editor's selection (or its caret's line) to the active terminal and runs it.</summary>
    public const string RunSelection = "terminal.runSelection";
}

/// <summary>What the Terminal tab is asked to do (<see cref="TerminalManager.ViewRequested"/>).</summary>
public enum TerminalViewRequest
{
    /// <summary>Show the Terminal tab and give the active terminal the keyboard.</summary>
    Show,

    /// <summary>Hide the bottom panel when the Terminal tab shows, else show it.</summary>
    Toggle,
}

/// <summary>
/// The integrated terminals (phase T), UI-free: the open terminals as tabs, the active one, their commands and keys on K's
/// contracts (<see cref="TerminalCommandIds"/>), the start rule (<see cref="TerminalLaunch"/>), the host chosen by
/// <c>CRAFT_TERMINAL_HOST</c>, and <see cref="IEditorTerminals"/> for plug-ins (<see cref="IEditorHost.Terminals"/>): a
/// plug-in opens a terminal and writes to it, never reads the keys. <see cref="Input"/> is where a terminal view sends
/// the keyboard: the user's bindings and the chords of the terminal's own commands and of
/// <c>CRAFT_TERMINAL_EDITOR_COMMANDS</c> run their commands; every other key goes to the shell. Used on the UI thread.
/// Disposing it ends every shell and takes its commands back.
/// </summary>
public sealed class TerminalManager : IEditorTerminals, IDisposable
{
    private readonly EditorHost _host;
    private readonly Func<CraftOptions> _options;
    private readonly IEditorClipboard _clipboard;
    private readonly Func<string?> _workspaceFolder;
    private readonly List<TerminalInstance> _terminals = [];
    private readonly Dictionary<TerminalInstance, Handle> _handles = [];
    private readonly List<string> _commandIds = [];
    private readonly List<KeyBinding> _bindings = [];
    private readonly List<(string Menu, string Command)> _menuItems = [];
    private int _nextId = 1;
    private bool _disposed;

    /// <summary>The terminals of <paramref name="host"/>, with <paramref name="options"/> read when one opens.</summary>
    /// <param name="host">The editor host (its dispatch, its log, its key routing, its active document).</param>
    /// <param name="options">The options in effect now (the shell's settings change the next terminal).</param>
    /// <param name="clipboard">The clipboard (copy and paste).</param>
    /// <param name="workspaceFolder">The open folder, or null (the host's <see cref="EditorHost.WorkspaceFolder"/> when null).</param>
    public TerminalManager(EditorHost host, Func<CraftOptions> options, IEditorClipboard clipboard, Func<string?>? workspaceFolder = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clipboard);
        _host = host;
        _options = options;
        _clipboard = clipboard;
        _workspaceFolder = workspaceFolder ?? (() => host.WorkspaceFolder);
        Input = new KeyRouter(this);
        RegisterCommands();
    }

    /// <summary>The open terminals, in their tabs' order.</summary>
    public IReadOnlyList<TerminalInstance> Terminals => _terminals;

    /// <summary>The terminal shown, or null.</summary>
    public TerminalInstance? ActiveTerminal { get; private set; }

    /// <summary>Where a terminal view sends its keyboard.</summary>
    public IEditorInputSink Input { get; }

    /// <summary>The open folder now, or null.</summary>
    public string? WorkspaceFolder => _workspaceFolder();

    /// <summary>Runs <see cref="TerminalCommandIds.New"/> through the host (a new terminal, shown; a failure said so).</summary>
    public Task<bool> ExecuteNewAsync() => _host.ExecuteCommandAsync(TerminalCommandIds.New);

    /// <summary>How the next terminal's size starts (the view sets what it measured).</summary>
    public TerminalSize LastSize { get; set; } = TerminalSize.Default;

    /// <summary>The home folder used when nothing else is chosen (the user's; a test sets its own).</summary>
    public string HomeFolder { get; set; } = TerminalLaunch.HomeFolder();

    /// <summary>Reads the app's environment (the process's; a test sets its own).</summary>
    public Func<IDictionary> ParentEnvironment { get; set; } = static () => Environment.GetEnvironmentVariables();

    /// <inheritdoc />
    IReadOnlyList<IEditorTerminal> IEditorTerminals.All => [.. _terminals.Select(HandleOf)];

    /// <inheritdoc />
    IEditorTerminal? IEditorTerminals.Active => ActiveTerminal is { } active ? HandleOf(active) : null;

    /// <summary>Raised when a terminal opened or closed, the active one changed, or a title or an exit changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a command asks the Terminal tab to show or toggle.</summary>
    public event EventHandler<TerminalViewRequest>? ViewRequested;

    /// <summary>
    /// Opens a terminal: in <paramref name="folder"/> when given (the explorer's "Open in terminal"), else by the rule
    /// (<c>CRAFT_TERMINAL_CWD</c>, the open folder, the home folder); it becomes the active one.
    /// </summary>
    /// <exception cref="InvalidOperationException">No shell could be started (the host, the program or the folder; the message says which).</exception>
    public TerminalInstance Open(string? folder = null, EditorTerminalRequest? request = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var options = _options();
        ITerminalHost host;
        try
        {
            host = TerminalHosts.Resolve(options.TerminalHost);
        }
        catch (NotRegisteredException error)
        {
            throw new InvalidOperationException($"CRAFT_TERMINAL_HOST: {error.Message}", error);
        }

        var start = TerminalLaunch.StartFolder(folder ?? request?.Folder, options.TerminalWorkingDirectory, _workspaceFolder(), HomeFolder);
        if (start.Note is { } note)
        {
            _host.Log(EditorLogLevel.Warning, $"Terminal: {note}; it starts in {start.Reason}, {start.Folder}.");
        }

        var shell = request?.Shell ?? TerminalLaunch.Shell(options, host);
        var environment = TerminalLaunch.Environment(ParentEnvironment(), options.TerminalEnvironment, request?.Environment, OperatingSystem.IsWindows());
        var terminal = TerminalInstance.Start(_nextId, host, new TerminalStartInfo(shell, start.Folder, LastSize, environment), options.TerminalScrollback, _host.Dispatch, request?.Name);
        _nextId++;
        terminal.TitleChanged += OnTerminalChanged;
        terminal.ExitedChanged += OnTerminalChanged;
        _terminals.Add(terminal);
        ActiveTerminal = terminal;
        _host.Log(EditorLogLevel.Information, $"Terminal {terminal.Id}: {shell.Program} {shell.Arguments} in {start.Folder} ({start.Reason}, host {host.Name}).");
        Changed?.Invoke(this, EventArgs.Empty);
        return terminal;
    }

    /// <inheritdoc />
    IEditorTerminal IEditorTerminals.Open(EditorTerminalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var terminal = Open(null, request);
        if (request.Show)
        {
            ViewRequested?.Invoke(this, TerminalViewRequest.Show);
        }

        return HandleOf(terminal);
    }

    /// <summary>Makes <paramref name="terminal"/> the one shown.</summary>
    public void Activate(TerminalInstance terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        if (_terminals.Contains(terminal) && !ReferenceEquals(ActiveTerminal, terminal))
        {
            ActiveTerminal = terminal;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Ends <paramref name="terminal"/>'s shell and closes its tab; the next one (else the previous) becomes active.</summary>
    public void Close(TerminalInstance terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        int index = _terminals.IndexOf(terminal);
        if (index < 0)
        {
            return;
        }

        _terminals.RemoveAt(index);
        _handles.Remove(terminal);
        terminal.TitleChanged -= OnTerminalChanged;
        terminal.ExitedChanged -= OnTerminalChanged;
        terminal.Dispose();
        if (ReferenceEquals(ActiveTerminal, terminal))
        {
            ActiveTerminal = _terminals.Count == 0 ? null : _terminals[Math.Min(index, _terminals.Count - 1)];
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether a key with <paramref name="chord"/> runs its command while a terminal has the keyboard (else it goes to the shell).</summary>
    public bool KeepsChord(KeyChord chord)
    {
        if (EditorKeyBindings.Find(chord) is not { CommandId.Length: > 0 } binding)
        {
            return false;
        }

        var id = binding.CommandId;
        if (id.StartsWith("terminal.", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var kept in _options().TerminalEditorCommands)
        {
            if (string.Equals(kept, id, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Ends every shell and takes the commands, bindings and menu items back.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var terminal in _terminals)
        {
            terminal.TitleChanged -= OnTerminalChanged;
            terminal.ExitedChanged -= OnTerminalChanged;
            terminal.Dispose();
        }

        _terminals.Clear();
        _handles.Clear();
        ActiveTerminal = null;
        foreach (var binding in _bindings)
        {
            EditorKeyBindings.Unregister(binding.Chord);
        }

        foreach (var (menu, command) in _menuItems)
        {
            EditorMenus.Unregister(menu, command);
        }

        foreach (var id in _commandIds)
        {
            EditorCommands.Unregister(id);
        }
    }

    private void OnTerminalChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private Handle HandleOf(TerminalInstance terminal)
    {
        if (!_handles.TryGetValue(terminal, out var handle))
        {
            handle = new Handle(this, terminal);
            _handles[terminal] = handle;
        }

        return handle;
    }

    private bool TryOpen(string? folder = null)
    {
        try
        {
            Open(folder);
            return true;
        }
        catch (InvalidOperationException error)
        {
            _host.Log(EditorLogLevel.Error, $"Terminal: {error.Message}", error);
            _ = _host.ShowMessageAsync("Craft: the terminal did not start", error.Message);
            return false;
        }
    }

    /// <summary>Opens a terminal in <paramref name="folder"/> and shows it (the explorer's "Open in terminal").</summary>
    public void OpenInFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (TryOpen(folder))
        {
            ViewRequested?.Invoke(this, TerminalViewRequest.Show);
        }
    }

    private void Step(int by)
    {
        if (_terminals.Count < 2 || ActiveTerminal is null)
        {
            return;
        }

        int index = _terminals.IndexOf(ActiveTerminal);
        ActiveTerminal = _terminals[(index + by + _terminals.Count) % _terminals.Count];
        Changed?.Invoke(this, EventArgs.Empty);
        ViewRequested?.Invoke(this, TerminalViewRequest.Show);
    }

    private async Task CopyAsync()
    {
        if (ActiveTerminal?.SelectedText() is { Length: > 0 } text)
        {
            await _clipboard.SetTextAsync(text);
        }
    }

    private async Task PasteAsync()
    {
        if (ActiveTerminal is { } terminal && await _clipboard.GetTextAsync() is { Length: > 0 } text)
        {
            terminal.Paste(text);
        }
    }

    private void RunSelection()
    {
        if (_host.ActiveDocument is not { } document)
        {
            return;
        }

        var snapshot = document.Document.Buffer.Current;
        var selection = document.Selection;
        string text;
        if (selection.IsEmpty)
        {
            var line = snapshot.GetLine(snapshot.GetLineIndex(Math.Clamp(selection.Caret, 0, snapshot.Length)));
            text = snapshot.GetText(line.Start, line.End - line.Start);
        }
        else
        {
            text = snapshot.GetText(selection.Start, selection.Length);
        }

        if (text.Trim().Length == 0 || (ActiveTerminal is null && !TryOpen()))
        {
            return;
        }

        var terminal = ActiveTerminal!;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        terminal.Write(System.Text.Encoding.UTF8.GetBytes(lines.Replace('\n', '\r') + "\r"));
        ViewRequested?.Invoke(this, TerminalViewRequest.Show);
    }

    private void RegisterCommands()
    {
        Command(TerminalCommandIds.New, "Terminal: New terminal", () =>
        {
            if (TryOpen())
            {
                ViewRequested?.Invoke(this, TerminalViewRequest.Show);
            }
        }, null, "Ctrl+Shift+Backquote");
        Command(TerminalCommandIds.Toggle, "Terminal: Show or hide the terminal", () =>
        {
            if (_terminals.Count == 0 && !TryOpen())
            {
                return;
            }

            ViewRequested?.Invoke(this, TerminalViewRequest.Toggle);
        }, null, "Ctrl+Backquote");
        Command(TerminalCommandIds.Next, "Terminal: Next terminal", () => Step(1), () => _terminals.Count > 1, null);
        Command(TerminalCommandIds.Previous, "Terminal: Previous terminal", () => Step(-1), () => _terminals.Count > 1, null);
        Command(TerminalCommandIds.Kill, "Terminal: Kill the active terminal", () =>
        {
            if (ActiveTerminal is { } terminal)
            {
                Close(terminal);
            }
        }, () => ActiveTerminal is not null, null);
        Command(TerminalCommandIds.Clear, "Terminal: Clear", () => ActiveTerminal?.Clear(), () => ActiveTerminal is not null, null);
        CommandAsync(TerminalCommandIds.Copy, "Terminal: Copy the selection", CopyAsync, () => ActiveTerminal?.Selection is { IsEmpty: false }, "Ctrl+Shift+C");
        CommandAsync(TerminalCommandIds.Paste, "Terminal: Paste", PasteAsync, () => ActiveTerminal is { HasExited: false }, "Ctrl+Shift+V");
        Command(TerminalCommandIds.RunSelection, "Terminal: Run the selection in the terminal", RunSelection, () => _host.ActiveDocument is not null, null);

        Menu(EditorMenus.View, TerminalCommandIds.Toggle, 5, 0);
        Menu(EditorMenus.View, TerminalCommandIds.New, 5, 1);
        Menu(EditorMenus.View, TerminalCommandIds.Next, 5, 2);
        Menu(EditorMenus.View, TerminalCommandIds.Previous, 5, 3);
        Menu(EditorMenus.Run, TerminalCommandIds.RunSelection, 0, 0);
        Menu(EditorMenus.Run, TerminalCommandIds.Clear, 1, 0);
        Menu(EditorMenus.Run, TerminalCommandIds.Kill, 1, 1);
    }

    private void Command(string id, string title, Action execute, Func<bool>? canExecute, string? chord) =>
        CommandAsync(id, title, () =>
        {
            execute();
            return Task.CompletedTask;
        }, canExecute, chord);

    private void CommandAsync(string id, string title, Func<Task> execute, Func<bool>? canExecute, string? chord)
    {
        EditorCommands.Register(new EditorCommand(id, title, (_, _) => execute(), canExecute is null ? null : _ => canExecute()));
        _commandIds.Add(id);
        if (chord is not null)
        {
            var binding = new KeyBinding(KeyChord.Parse(chord), id);
            EditorKeyBindings.Register(binding);
            _bindings.Add(binding);
        }
    }

    private void Menu(string menu, string command, int group, int order)
    {
        EditorMenus.Register(new MenuItem(menu, command, null, order, group));
        _menuItems.Add((menu, command));
    }

    /// <summary>A plug-in's hold on a terminal: it writes and shows, and stops once the terminal closed.</summary>
    private sealed class Handle(TerminalManager owner, TerminalInstance terminal) : IEditorTerminal
    {
        public string Title => terminal.Title;

        public string WorkingDirectory => terminal.WorkingDirectory;

        public Task<int> Exited => terminal.Exited;

        public bool Write(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            if (terminal.HasExited || !owner._terminals.Contains(terminal))
            {
                return false;
            }

            terminal.Write(TerminalKeys.EncodeText(text));
            return true;
        }

        public void Show()
        {
            if (owner._terminals.Contains(terminal))
            {
                owner.Activate(terminal);
                owner.ViewRequested?.Invoke(owner, TerminalViewRequest.Show);
            }
        }
    }

    /// <summary>
    /// The terminal view's keyboard: through the host's router (the user's bindings first), with the active terminal as
    /// the "editor": a chord of a kept command is declined (so its binding runs), every other key goes to the shell.
    /// </summary>
    private sealed class KeyRouter(TerminalManager owner) : IEditorInputSink
    {
        private readonly TerminalSink _sink = new(owner);

        public bool OnKey(EditorKey key, KeyModifiers modifiers) => owner._host.HandleKey(key, modifiers, _sink);

        public void OnText(string text) => owner.ActiveTerminal?.SendText(text);
    }

    private sealed class TerminalSink(TerminalManager owner) : IEditorInputSink
    {
        public bool OnKey(EditorKey key, KeyModifiers modifiers) =>
            !owner.KeepsChord(new KeyChord(key, modifiers)) && (owner.ActiveTerminal?.HandleKey(key, modifiers) ?? false);

        public void OnText(string text) => owner.ActiveTerminal?.SendText(text);
    }
}
