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

    /// <summary>Picks a profile and opens a terminal of it (B9).</summary>
    public const string NewWithProfile = "terminal.newWithProfile";

    /// <summary>Picks the default profile (B9: "Set default profile…"), written to the workspace's settings.</summary>
    public const string SelectDefaultProfile = "terminal.selectDefaultProfile";

    /// <summary>A second terminal beside the active one (or the context menu's), in the same split group (B9).</summary>
    public const string Split = "terminal.split";

    /// <summary>Renames the active terminal's tab (or the context menu's).</summary>
    public const string Rename = "terminal.rename";

    /// <summary>Selects everything the terminal keeps.</summary>
    public const string SelectAll = "terminal.selectAll";

    /// <summary>The prefix of the per-profile commands ("terminal.new.pwsh": "Terminal: New PowerShell 7").</summary>
    public const string NewProfilePrefix = "terminal.new.";

    /// <summary>The command that opens a terminal of the profile <paramref name="id"/>.</summary>
    public static string NewProfile(string id) => NewProfilePrefix + id;
}

/// <summary>A row of the terminals' tabs or list (B9): one split group, its terminals, how it shows.</summary>
/// <param name="Group">The group's id.</param>
/// <param name="Terminals">Its terminals, side by side in this order.</param>
/// <param name="Label">The short names ("pwsh", "pwsh | cmd" for a split).</param>
/// <param name="Icon">The first terminal's profile icon (SVG path data).</param>
/// <param name="Tooltip">The full titles, the profile, the folder, how it ended.</param>
/// <param name="Active">Whether it is the group shown.</param>
/// <param name="Busy">Whether a command runs in one of them (the busy dot).</param>
/// <param name="Ended">Whether every shell of it ended.</param>
/// <param name="ReadOnly">Whether it is a plug-in's output tab.</param>
public sealed record TerminalTabRow(int Group, IReadOnlyList<TerminalInstance> Terminals, string Label, string Icon, string Tooltip, bool Active, bool Busy, bool Ended, bool ReadOnly);

/// <summary>What the Terminal tab is asked to do (<see cref="TerminalManager.ViewRequested"/>).</summary>
public enum TerminalViewRequest
{
    /// <summary>Show the Terminal tab and give the active terminal the keyboard.</summary>
    Show,

    /// <summary>Hide the bottom panel when the Terminal tab shows, else show it.</summary>
    Toggle,

    /// <summary>Show the Terminal tab, leaving the keyboard where it is (a plug-in's output tab).</summary>
    Reveal,
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
    private readonly Dictionary<TerminalInstance, Output> _outputs = [];
    private readonly List<string> _commandIds = [];
    private readonly List<KeyBinding> _bindings = [];
    private readonly List<(string Menu, string Command)> _menuItems = [];
    private readonly List<string> _profileCommands = [];
    private readonly List<(string Menu, string Command)> _profileMenuItems = [];
    private IReadOnlyList<TerminalProfile> _found = [];
    private string _profileSignature = "";
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
        SettingChoiceSources.Register(new SettingChoices(SettingChoiceSources.TerminalProfiles, () => [.. Profiles.Select(p => p.Name)], "the terminal profiles: found on this machine and your own"));
        RegisterProfileCommands();
        ProfilesReady = RefreshProfilesAsync();
    }

    /// <summary>Completes when the profiles of the machine have been found (off the UI thread) and their commands registered.</summary>
    public Task ProfilesReady { get; private set; }

    /// <summary>Asks, prompts and tells (the default profile's picker, Rename); null: those commands do nothing.</summary>
    public IShellUi? Ui { get; set; }

    /// <summary>Writes the default profile's name to the settings (the workspace's when a folder is open): null when done, else why not.</summary>
    public Func<string, string?>? StoreDefaultProfile { get; set; }

    /// <summary>Every profile: the machine's found ones, the user's (<c>CRAFT_TERMINAL_PROFILES</c>), <c>CRAFT_TERMINAL_SHELL</c>'s.</summary>
    public IReadOnlyList<TerminalProfile> Profiles => TerminalLaunch.AllProfiles(_found, _options());

    /// <summary>The profile "+" and New terminal open (<see cref="TerminalLaunch.DefaultProfile"/>).</summary>
    public TerminalProfile DefaultProfile => TerminalLaunch.DefaultProfile(Profiles, _options(), Host());

    /// <summary>Finds the machine's profiles again (off the UI thread), then registers their commands on the UI thread.</summary>
    public Task RefreshProfilesAsync()
    {
        ProfilesReady = DiscoverAsync();
        return ProfilesReady;
    }

    /// <summary>The options changed: the profiles' commands follow the user's profiles.</summary>
    public void ApplyOptions() => RegisterProfileCommands();

    /// <summary>Whether a command runs in <paramref name="terminal"/> now (the tab's busy dot).</summary>
    public bool IsBusy(TerminalInstance terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        var options = _options();
        return terminal.IsBusy(terminal.Clock, terminal.Clock.GetTimestamp(), TimeSpan.FromMilliseconds(options.TerminalBusyAfterMilliseconds),
            TimeSpan.FromMilliseconds(options.TerminalBusyQuietMilliseconds));
    }

    /// <summary>The terminals of <paramref name="terminal"/>'s split group, in their order.</summary>
    public IReadOnlyList<TerminalInstance> GroupOf(TerminalInstance? terminal) =>
        terminal is null ? [] : [.. _terminals.Where(t => t.Group == terminal.Group)];

    /// <summary>The tabs (or the list's rows): one per split group, in the order of their first terminal.</summary>
    public IReadOnlyList<TerminalTabRow> Rows()
    {
        var rows = new List<TerminalTabRow>();
        var seen = new HashSet<int>();
        foreach (var first in _terminals)
        {
            if (!seen.Add(first.Group))
            {
                continue;
            }

            var group = GroupOf(first);
            var label = string.Join(" | ", group.Select(t => t.ShortTitle));
            var tip = string.Join("\n", group.Select(Describe));
            rows.Add(new TerminalTabRow(first.Group, group, label, first.Profile?.Icon ?? TerminalLaunch.IconOf(first.Shell.Program), tip,
                ActiveTerminal is { } active && active.Group == first.Group, group.Any(IsBusy), group.All(t => t.HasExited), group.All(t => t.IsReadOnly)));
        }

        return rows;
    }

    private static string Describe(TerminalInstance terminal)
    {
        if (terminal.IsReadOnly)
        {
            return terminal.Title + ": a plug-in's output; nothing typed here runs (✕ closes it; it opens again with the next output)";
        }

        var text = terminal.Title + (terminal.Profile is { } profile ? $" · {profile.Name}" : "") + " · " + terminal.WorkingDirectory;
        return terminal.ExitCode is { } code ? text + $" · ended with exit code {code}" : text;
    }

    private ITerminalHost Host()
    {
        try
        {
            return TerminalHosts.Resolve(_options().TerminalHost);
        }
        catch (NotRegisteredException)
        {
            return TerminalHosts.Get(TerminalHosts.Pipe);
        }
    }

    private async Task DiscoverAsync()
    {
        IReadOnlyList<TerminalProfile> found;
        try
        {
            found = await TerminalProfiles.DiscoverAsync((source, error) => _host.Dispatch(() =>
                _host.Log(EditorLogLevel.Warning, $"Terminal: the profile source '{source}' failed: {error.Message}", error))).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.Dispatch(() =>
        {
            try
            {
                if (!_disposed)
                {
                    _found = found;
                    _host.Log(EditorLogLevel.Information, $"Terminal: profiles found: {MachineTerminalProfiles.Describe(found)}.");
                    RegisterProfileCommands();
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            finally
            {
                done.TrySetResult();
            }
        });
        await done.Task.ConfigureAwait(false);
    }

    // One command a profile ("Terminal: New PowerShell 7"), and its row in the "+" chevron's menu; taken back and made
    // again when the profiles change.
    private void RegisterProfileCommands()
    {
        if (_disposed)
        {
            return;
        }

        var profiles = Profiles;
        var signature = string.Join("|", profiles.Select(p => p.Id + "=" + p.Name));
        if (signature == _profileSignature)
        {
            return;
        }

        _profileSignature = signature;
        foreach (var (menu, command) in _profileMenuItems)
        {
            EditorMenus.Unregister(menu, command);
        }

        foreach (var id in _profileCommands)
        {
            EditorCommands.Unregister(id);
        }

        _profileMenuItems.Clear();
        _profileCommands.Clear();
        int order = 0;
        foreach (var profile in profiles)
        {
            var chosen = profile;
            var id = TerminalCommandIds.NewProfile(profile.Id);
            EditorCommands.Register(new EditorCommand(id, "Terminal: New " + profile.Name, (_, _) =>
            {
                OpenShown(chosen);
                return Task.CompletedTask;
            }));
            _profileCommands.Add(id);
            EditorMenus.Register(new MenuItem(EditorContextMenus.TerminalProfiles, id, null, order++, 0));
            _profileMenuItems.Add((EditorContextMenus.TerminalProfiles, id));
        }
    }

    private void OpenShown(TerminalProfile profile, string? folder = null, int? group = null)
    {
        try
        {
            Open(profile, folder, group);
            ViewRequested?.Invoke(this, TerminalViewRequest.Show);
        }
        catch (InvalidOperationException error)
        {
            _host.Log(EditorLogLevel.Error, $"Terminal: {error.Message}", error);
            _ = _host.ShowMessageAsync("Craft: the terminal did not start", error.Message);
        }
    }

    /// <summary>Opens a terminal of <paramref name="profile"/> (in <paramref name="folder"/>, else by the rule; in split group <paramref name="group"/>, else its own).</summary>
    /// <exception cref="InvalidOperationException">The shell could not be started.</exception>
    public TerminalInstance Open(TerminalProfile profile, string? folder = null, int? group = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return OpenCore(folder ?? profile.Folder, null, profile, group);
    }

    /// <summary>Makes <paramref name="profile"/> the default (written to the settings through <see cref="StoreDefaultProfile"/>): null when done, else why not.</summary>
    public string? SetDefaultProfile(TerminalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (StoreDefaultProfile is not { } store)
        {
            return "This window cannot write settings.";
        }

        var problem = store(profile.Name);
        if (problem is null)
        {
            _host.Log(EditorLogLevel.Information, $"Terminal: the default profile is {profile.Name}.");
            RegisterProfileCommands();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return problem;
    }

    /// <summary>Splits <paramref name="terminal"/>'s group: a terminal of its profile, in its folder, beside it.</summary>
    public TerminalInstance? Split(TerminalInstance? terminal = null)
    {
        terminal ??= ActiveTerminal;
        if (terminal is null || terminal.IsReadOnly)
        {
            return OpenDefaultShown();
        }

        var profile = terminal.Profile ?? DefaultProfile;
        string folder;
        lock (terminal.Screen.Sync)
        {
            folder = terminal.Screen.WorkingDirectory is { } current && Directory.Exists(current) ? current : terminal.WorkingDirectory;
        }

        try
        {
            var split = Open(profile, folder, terminal.Group);
            ViewRequested?.Invoke(this, TerminalViewRequest.Show);
            return split;
        }
        catch (InvalidOperationException error)
        {
            _host.Log(EditorLogLevel.Error, $"Terminal: {error.Message}", error);
            _ = _host.ShowMessageAsync("Craft: the terminal did not start", error.Message);
            return null;
        }
    }

    private TerminalInstance? OpenDefaultShown()
    {
        if (!TryOpen())
        {
            return null;
        }

        ViewRequested?.Invoke(this, TerminalViewRequest.Show);
        return ActiveTerminal;
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
    public TerminalInstance Open(string? folder = null, EditorTerminalRequest? request = null) =>
        OpenCore(folder, request, request?.Shell is null ? DefaultProfile : null, null);

    private TerminalInstance OpenCore(string? folder, EditorTerminalRequest? request, TerminalProfile? profile, int? group)
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

        var shell = request?.Shell ?? profile?.Shell ?? TerminalLaunch.Shell(options, host);
        var environment = TerminalLaunch.Environment(ParentEnvironment(), options.TerminalEnvironment, request?.Environment ?? profile?.Environment, OperatingSystem.IsWindows());
        var terminal = TerminalInstance.Start(_nextId, host, new TerminalStartInfo(shell, start.Folder, LastSize, environment), options.TerminalScrollback, _host.Dispatch, request?.Name);
        _nextId++;
        terminal.Profile = profile;
        lock (terminal.Screen.Sync)
        {
            terminal.Screen.DefaultCursorStyle = TerminalLaunch.CursorStyle(options.TerminalCursor);
        }

        terminal.TitleChanged += OnTerminalChanged;
        terminal.ExitedChanged += OnTerminalChanged;
        int at = _terminals.Count;
        if (group is { } splitOf && _terminals.FindLastIndex(t => t.Group == splitOf) is >= 0 and var last)
        {
            terminal.Group = splitOf;
            at = last + 1;                                   // beside the group's last terminal
        }

        _terminals.Insert(at, terminal);
        ActiveTerminal = terminal;
        _host.Log(EditorLogLevel.Information, $"Terminal {terminal.Id}: {(profile is null ? "" : profile.Name + ", ")}{shell.Program} {shell.Arguments} in {start.Folder} ({start.Reason}, host {host.Name}).");
        Changed?.Invoke(this, EventArgs.Empty);
        return terminal;
    }

    /// <summary>
    /// The read-only output tab named <paramref name="name"/> (one per name, opened when missing, not made active over a
    /// shell the user is in): what a plug-in appends shows there, nothing typed goes anywhere.
    /// </summary>
    public IEditorTerminalOutput OpenOutput(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var open in _terminals)
        {
            if (open.IsReadOnly && string.Equals(open.Title, name, StringComparison.Ordinal) && _outputs.TryGetValue(open, out var existing))
            {
                return existing;
            }
        }

        var session = new OutputTerminalSession();
        var terminal = TerminalInstance.Output(_nextId, session, LastSize, _options().TerminalScrollback, _host.Dispatch, name, _workspaceFolder() ?? HomeFolder);
        _nextId++;
        terminal.TitleChanged += OnTerminalChanged;
        terminal.ExitedChanged += OnTerminalChanged;
        _terminals.Add(terminal);
        ActiveTerminal ??= terminal;
        var output = new Output(this, terminal, session);
        _outputs[terminal] = output;
        Changed?.Invoke(this, EventArgs.Empty);
        return output;
    }

    /// <inheritdoc />
    IEditorTerminalOutput? IEditorTerminals.OpenOutput(string name) => OpenOutput(name);

    /// <summary>
    /// A tab over a program a plug-in started (X part 6b: a run that reads input): its output drawn, what the user types sent
    /// to its input through <see cref="ProgramTerminalSession"/>'s line discipline. A tab of the same name that ended or is an
    /// output tab is replaced in its place; the new tab becomes the active one.
    /// </summary>
    public IEditorTerminalProgram OpenProgram(EditorProgramRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ObjectDisposedException.ThrowIf(_disposed, this);
        int at = _terminals.FindIndex(t => (t.IsReadOnly || (t.IsProgram && t.HasExited)) && string.Equals(t.Title, request.Name, StringComparison.Ordinal));
        if (at >= 0)
        {
            var old = _terminals[at];
            _terminals.RemoveAt(at);
            _outputs.Remove(old);
            old.TitleChanged -= OnTerminalChanged;
            old.ExitedChanged -= OnTerminalChanged;
            old.Dispose();
        }
        else
        {
            at = _terminals.Count;
        }

        var session = new ProgramTerminalSession(request);
        var terminal = TerminalInstance.Program(_nextId, session, LastSize, _options().TerminalScrollback, _host.Dispatch, request.Name, request.Folder ?? _workspaceFolder() ?? HomeFolder);
        _nextId++;
        terminal.TitleChanged += OnTerminalChanged;
        terminal.ExitedChanged += OnTerminalChanged;
        _terminals.Insert(at, terminal);
        ActiveTerminal = terminal;
        var program = new ProgramTab(this, terminal, session);
        Changed?.Invoke(this, EventArgs.Empty);
        return program;
    }

    /// <inheritdoc />
    IEditorTerminalProgram? IEditorTerminals.OpenProgram(EditorProgramRequest request) => OpenProgram(request);

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
        _outputs.Remove(terminal);
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
        _outputs.Clear();
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

        foreach (var (menu, command) in _profileMenuItems)
        {
            EditorMenus.Unregister(menu, command);
        }

        foreach (var id in _profileCommands)
        {
            EditorCommands.Unregister(id);
        }

        _profileMenuItems.Clear();
        _profileCommands.Clear();
        SettingChoiceSources.Unregister(SettingChoiceSources.TerminalProfiles);
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
        if (Target()?.SelectedText() is { Length: > 0 } text)
        {
            await CopyTextAsync(text);
        }
    }

    /// <summary>
    /// Ctrl+C in a terminal (the owner's note 20): with a selection and <see cref="CraftOptions.TerminalCtrlCCopies"/>, the
    /// selection goes to the clipboard and ends (the key does not reach the shell); with none, false: the shell gets ^C.
    /// </summary>
    public bool TryCopyOnCtrlC()
    {
        if (!_options().TerminalCtrlCCopies || ActiveTerminal is not { } terminal || terminal.SelectedText() is not { Length: > 0 } text)
        {
            return false;
        }

        terminal.ClearSelection();
        _ = CopyTextAsync(text);
        return true;
    }

    // The clipboard is reached through the dispatcher (DispatchedClipboard): a failure is logged, never thrown into the key's handler.
    private async Task CopyTextAsync(string text)
    {
        try
        {
            await _clipboard.SetTextAsync(text);
        }
#pragma warning disable CA1031 // A copy that fails is said in Output; the terminal goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Log(EditorLogLevel.Warning, $"Terminal: the selection could not be copied: {error.Message}", error);
        }
    }

    private async Task PasteAsync()
    {
        if (Target() is { } terminal && await _clipboard.GetTextAsync() is { Length: > 0 } text)
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

        if (text.Trim().Length == 0)
        {
            return;
        }

        // A shell's terminal: the active one, else the last shell open, else a new one (an output tab runs nothing).
        if (ActiveTerminal is not { IsReadOnly: false } && _terminals.LastOrDefault(t => !t.IsReadOnly) is { } shellTerminal)
        {
            Activate(shellTerminal);
        }

        if (ActiveTerminal is not { IsReadOnly: false } && !TryOpen())
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
            if (!_terminals.Exists(t => !t.IsReadOnly) && !TryOpen())
            {
                return;                      // only output tabs: the toggle opens the user's shell as before
            }

            ViewRequested?.Invoke(this, TerminalViewRequest.Toggle);
        }, null, "Ctrl+Backquote");
        Command(TerminalCommandIds.Next, "Terminal: Next terminal", () => Step(1), () => _terminals.Count > 1, null);
        Command(TerminalCommandIds.Previous, "Terminal: Previous terminal", () => Step(-1), () => _terminals.Count > 1, null);
        Command(TerminalCommandIds.Kill, "Terminal: Kill the active terminal", () =>
        {
            if (Target() is { } terminal)
            {
                Close(terminal);
            }
        }, () => Target() is not null, null);
        Command(TerminalCommandIds.Clear, "Terminal: Clear", () => Target()?.Clear(), () => Target() is not null, null);
        CommandAsync(TerminalCommandIds.Copy, "Terminal: Copy the selection", CopyAsync, () => Target()?.Selection is { IsEmpty: false }, "Ctrl+Shift+C");
        CommandAsync(TerminalCommandIds.Paste, "Terminal: Paste", PasteAsync, () => ActiveTerminal is { HasExited: false }, "Ctrl+Shift+V");
        Command(TerminalCommandIds.RunSelection, "Terminal: Run the selection in the terminal", RunSelection, () => _host.ActiveDocument is not null, null);

        CommandAsync(TerminalCommandIds.NewWithProfile, "Terminal: New terminal with a profile…", NewWithProfileAsync, null, null);
        CommandAsync(TerminalCommandIds.SelectDefaultProfile, "Terminal: Select the default profile…", SelectDefaultProfileAsync, null, null);
        Command(TerminalCommandIds.Split, "Terminal: Split the terminal", () => Split(Target()), null, null);
        CommandAsync(TerminalCommandIds.Rename, "Terminal: Rename…", RenameAsync, () => Target() is { IsReadOnly: false }, null);
        Command(TerminalCommandIds.SelectAll, "Terminal: Select all", () => Target()?.SelectAll(), () => Target() is not null, null);

        Menu(EditorMenus.View, TerminalCommandIds.Toggle, 5, 0);
        Menu(EditorMenus.View, TerminalCommandIds.New, 5, 1);
        Menu(EditorMenus.View, TerminalCommandIds.Next, 5, 2);
        Menu(EditorMenus.View, TerminalCommandIds.Previous, 5, 3);
        Menu(EditorMenus.Run, TerminalCommandIds.RunSelection, 0, 0);
        Menu(EditorMenus.Run, TerminalCommandIds.Clear, 1, 0);
        Menu(EditorMenus.Run, TerminalCommandIds.Kill, 1, 1);
        Menu(EditorMenus.View, TerminalCommandIds.Split, 5, 4);

        // B9: the terminal's context menu, and the chevron's rows after the profiles.
        Menu(EditorContextMenus.Terminal, TerminalCommandIds.Copy, 0, 0);
        Menu(EditorContextMenus.Terminal, TerminalCommandIds.Paste, 0, 1);
        Menu(EditorContextMenus.Terminal, TerminalCommandIds.SelectAll, 0, 2);
        Menu(EditorContextMenus.Terminal, TerminalCommandIds.Clear, 1, 0);
        Menu(EditorContextMenus.Terminal, TerminalCommandIds.Split, 2, 0);
        Menu(EditorContextMenus.Terminal, TerminalCommandIds.Rename, 2, 1);
        Menu(EditorContextMenus.Terminal, TerminalCommandIds.Kill, 2, 2);
        Menu(EditorContextMenus.TerminalProfiles, TerminalCommandIds.SelectDefaultProfile, 1, 0);
    }

    // The context menu's terminal (its target), else the active one.
    private TerminalInstance? Target() =>
        EditorContextMenus.Target is { Menu: EditorContextMenus.Terminal, Item: TerminalInstance target } && _terminals.Contains(target) ? target : ActiveTerminal;

    private async Task NewWithProfileAsync()
    {
        var profiles = Profiles;
        if (Ui is not { } ui || profiles.Count == 0)
        {
            OpenShown(DefaultProfile);
            return;
        }

        int picked = await ui.PickAsync("New terminal with a profile", [.. profiles.Select(p => p.Name)]);
        if (picked >= 0 && picked < profiles.Count)
        {
            OpenShown(profiles[picked]);
        }
    }

    private async Task SelectDefaultProfileAsync()
    {
        var profiles = Profiles;
        if (Ui is not { } ui || profiles.Count == 0)
        {
            return;
        }

        var current = DefaultProfile;
        int picked = await ui.PickAsync("Default terminal profile (now " + current.Name + ")", [.. profiles.Select(p => p.Name)]);
        if (picked >= 0 && picked < profiles.Count && SetDefaultProfile(profiles[picked]) is { } problem)
        {
            await ui.ShowMessageAsync("Craft: the default profile was not written", problem);
        }
    }

    private async Task RenameAsync()
    {
        if (Target() is not { IsReadOnly: false } terminal || Ui is not { } ui)
        {
            return;
        }

        var name = await ui.PromptAsync("Rename the terminal", "A name for its tab; empty gives it back the shell's own.", "Name", terminal.UserName ?? terminal.ShortTitle, "Rename");
        if (name is not null && _terminals.Contains(terminal))
        {
            terminal.Rename(name);
        }
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

    /// <summary>A plug-in's hold on its read-only output tab: it appends (any thread) and shows; it stops once the tab closed.</summary>
    private sealed class Output(TerminalManager owner, TerminalInstance terminal, OutputTerminalSession session) : IEditorTerminalOutput
    {
        public string Title => terminal.Title;

        public bool IsOpen => session.IsOpen;

        public bool Append(string text) => session.Append(text);

        public void Show()
        {
            if (owner._terminals.Contains(terminal))
            {
                owner.Activate(terminal);
                owner.ViewRequested?.Invoke(owner, TerminalViewRequest.Reveal);
            }
        }
    }

    /// <summary>A plug-in's hold on its program's tab: notes and the end (any thread); Show gives the tab the keyboard.</summary>
    private sealed class ProgramTab(TerminalManager owner, TerminalInstance terminal, ProgramTerminalSession session) : IEditorTerminalProgram
    {
        public string Title => terminal.Title;

        public bool IsOpen => session.IsOpen;

        public Task OutputEnded => session.OutputEnded;

        public bool Append(string text) => session.Append(text);

        public void End(string? text = null, int exitCode = 0) => session.End(text, exitCode);

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
            (key == EditorKey.C && modifiers == KeyModifiers.Control && owner.TryCopyOnCtrlC())
            || (!owner.KeepsChord(new KeyChord(key, modifiers)) && (owner.ActiveTerminal?.HandleKey(key, modifiers) ?? false));

        public void OnText(string text) => owner.ActiveTerminal?.SendText(text);
    }
}
