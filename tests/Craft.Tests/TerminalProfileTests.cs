// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Settings;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Shell;
using Craft.Editor.Terminal;
using Craft.Tests.Editor;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// B9: the terminal's shell profiles (found on a fake machine, the user's, <c>CRAFT_TERMINAL_SHELL</c> as one), the
/// default chosen and kept, the menus' rows, a terminal started per profile through the "pipe" host, rename, splits,
/// the tabs and the list, short names, the cursor's default shape, the busy dot, and the empty state.
/// </summary>
[TestGroup("Terminal")]
public static class TerminalProfileTests
{
    private static TerminalMachine WindowsMachine(params string[] files)
    {
        var set = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        return new TerminalMachine
        {
            IsWindows = true,
            Variable = name => name switch
            {
                "PATH" => @"C:\Windows\System32;C:\Program Files\PowerShell\7",
                "PATHEXT" => ".COM;.EXE;.BAT;.CMD",
                "SystemRoot" => @"C:\Windows",
                "ComSpec" => @"C:\Windows\System32\cmd.exe",
                "ProgramFiles" => @"C:\Program Files",
                "ProgramFiles(x86)" => @"C:\Program Files (x86)",
                "LOCALAPPDATA" => @"C:\Users\me\AppData\Local",
                _ => null,
            },
            FileExists = path => set.Contains(path.Replace('/', '\\')),
            Folders = path => path.Replace('/', '\\') switch
            {
                @"C:\Program Files\Microsoft Visual Studio" => [@"C:\Program Files\Microsoft Visual Studio\2022"],
                @"C:\Program Files\Microsoft Visual Studio\2022" => [@"C:\Program Files\Microsoft Visual Studio\2022\Community"],
                _ => [],
            },
            WslDistributions = () => ["Ubuntu-24.04", "Debian"],
            GitFolder = () => @"D:\Tools\Git",
        };
    }

    [Test]
    public static void ProfilesAreFoundOnAFakeWindowsMachine()
    {
        var machine = WindowsMachine(
            @"C:\Program Files\PowerShell\7\pwsh.exe",
            @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            @"C:\Windows\System32\cmd.exe",
            @"D:\Tools\Git\bin\bash.exe",
            @"C:\Windows\System32\wsl.exe",
            @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\Launch-VsDevShell.ps1",
            @"C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat");
        var found = new MachineTerminalProfiles("machine", machine).Find();
        Check.SequenceEqual(["pwsh", "powershell", "cmd", "git-bash", "wsl-ubuntu-24-04", "wsl-debian", "vs-2022-community-pwsh", "vs-2022-community-cmd"], [.. found.Select(p => p.Id)],
            "PowerShell 7, Windows PowerShell, the Command Prompt, Git Bash, each WSL distribution, VS's two developer shells, in that order");
        var pwsh = found[0];
        Check.True(pwsh is { Name: "PowerShell 7", Short: "pwsh", Icon: TerminalIcons.PowerShell } && pwsh.Shell.Arguments == "-NoLogo" && pwsh.Shell.Program.EndsWith("pwsh.exe", StringComparison.Ordinal),
            "PowerShell 7 from the PATH, -NoLogo, its icon and short name");
        Check.True(found[3].Shell.Program == Path.Combine(@"D:\Tools\Git", "bin", "bash.exe") && found[3].Shell.Arguments == "--login -i" && found[3].Short == "bash", "Git Bash from the registry's InstallPath, a login shell");
        Check.True(found[4] is { Name: "Ubuntu-24.04 (WSL)", Short: "ubuntu", Icon: TerminalIcons.Linux } && found[4].Shell.Arguments == "-d Ubuntu-24.04", "a WSL distribution through wsl.exe -d");
        Check.True(found[6].Name == "Developer PowerShell for VS 2022 Community" && found[6].Shell.Arguments.Contains("Launch-VsDevShell.ps1", StringComparison.Ordinal)
            && found[7].Name == "Developer Command Prompt for VS 2022 Community" && found[7].Shell.Arguments.StartsWith("/k", StringComparison.Ordinal),
            "the developer shells of each Visual Studio edition");

        var bare = new MachineTerminalProfiles("machine", WindowsMachine(@"C:\Windows\System32\cmd.exe")).Find();
        Check.SequenceEqual(["cmd"], [.. bare.Select(p => p.Id)], "a machine with only cmd: only cmd (no WSL without wsl.exe, nothing guessed)");

        var unix = new MachineTerminalProfiles("machine", new TerminalMachine
        {
            Variable = name => name switch { "SHELL" => "/usr/bin/zsh", "PATH" => "/usr/bin:/bin", _ => null },
            FileExists = path => path is "/usr/bin/zsh" or "/bin/bash" or "/bin/sh" || path == Path.Combine("/bin", "bash") || path == Path.Combine("/bin", "sh"),
        }).Find();
        Check.SequenceEqual(["zsh", "bash", "sh"], [.. unix.Select(p => p.Id)], "elsewhere: the login shell first, then the others found, each once");
        Check.True(unix[0].Shell.Arguments == "-l" && unix[2].Shell.Arguments.Length == 0, "bash and zsh as login shells");
    }

    [Test]
    public static async Task DiscoveryPassesOverABrokenSourceAndKeepsTheFirstOfAnId()
    {
        TerminalProfiles.Register(new FixedSource("test-a", [new TerminalProfile("same", "First", new TerminalShell("a"))]));
        TerminalProfiles.Register(new ThrowingSource());
        TerminalProfiles.Register(new FixedSource("test-b", [new TerminalProfile("same", "Second", new TerminalShell("b")), new TerminalProfile("other", "Other", new TerminalShell("c"))]));
        try
        {
            var failures = new List<string>();
            var found = await TerminalProfiles.DiscoverAsync((source, _) => failures.Add(source));
            Check.True(found.Single(p => p.Id == "same").Name == "First" && found.Any(p => p.Id == "other"), "the first source's profile of an id wins; the rest still count");
            Check.SequenceEqual(["test-broken"], failures, "the source that threw is named and passed over");
            Check.True(found.Single(p => p.Id == "other").Source == "test-b", "a profile carries the source that found it");
            Check.True(TerminalProfiles.LastFound.Contains("Other") && SettingChoiceSources.Get(SettingChoiceSources.TerminalProfiles).Choices.Contains("Other"),
                "the settings page's drop-down lists what was found");
            Check.Throws<NotRegisteredException>(() => TerminalProfiles.Get("no-such-source"), "an unknown source is not registered");
        }
        finally
        {
            TerminalProfiles.Unregister("test-a");
            TerminalProfiles.Unregister("test-broken");
            TerminalProfiles.Unregister("test-b");
        }
    }

    [Test]
    public static void TheUsersProfilesTheShellSettingAndTheDefault()
    {
        var options = CraftOptions.FromVariables(name => name switch
        {
            "CRAFT_TERMINAL_PROFILES" => "My Bash=\"C:\\Program Files\\Git\\bin\\bash.exe\" --login|Node=node --interactive",
            _ => null,
        });
        var user = TerminalLaunch.UserProfiles(options);
        Check.True(user.Count == 2 && user[0] is { Id: "user-my-bash", Name: "My Bash", Source: "user", Icon: TerminalIcons.Bash }
            && user[0].Shell == new TerminalShell(@"C:\Program Files\Git\bin\bash.exe", "--login") && user[1].Shell == new TerminalShell("node", "--interactive"),
            "the user's profiles: a quoted program keeps its spaces");
        var found = new[] { new TerminalProfile("cmd", "Command Prompt", new TerminalShell("cmd.exe")), new TerminalProfile("pwsh", "PowerShell 7", new TerminalShell("pwsh.exe", "-NoLogo")) };
        var pipe = TerminalHosts.Get(TerminalHosts.Pipe);
        var all = TerminalLaunch.AllProfiles(found, options);
        Check.SequenceEqual(["cmd", "pwsh", "user-my-bash", "user-node"], [.. all.Select(p => p.Id)], "the found ones, then the user's");

        var chosen = TerminalLaunch.DefaultProfile(all, options with { TerminalDefaultProfile = "powershell 7" }, pipe);
        Check.Equal("pwsh", chosen.Id, "CRAFT_TERMINAL_DEFAULT_PROFILE names a profile by its name (ignoring case)");
        Check.Equal("user-node", TerminalLaunch.DefaultProfile(all, options with { TerminalDefaultProfile = "user-node" }, pipe).Id, "or by its id");

        var shellSet = options with { TerminalShell = "/opt/fish", TerminalArguments = "-i" };
        var withShell = TerminalLaunch.AllProfiles(found, shellSet);
        var setting = withShell[^1];
        Check.True(setting is { Id: "setting", Name: "/opt/fish", Source: "setting" } && setting.Shell == new TerminalShell("/opt/fish", "-i"), "CRAFT_TERMINAL_SHELL is a profile named by its path, with CRAFT_TERMINAL_ARGS");
        Check.Equal("setting", TerminalLaunch.DefaultProfile(withShell, shellSet, pipe).Id, "and the default while no default profile is set");
        Check.Equal("setting", TerminalLaunch.DefaultProfile(withShell, shellSet with { TerminalDefaultProfile = "gone" }, pipe).Id, "a default naming no profile falls back to the shell setting");
        var hostDefault = TerminalLaunch.DefaultProfile([], new CraftOptions(), pipe);
        Check.True(hostDefault.Shell == pipe.DefaultShell() && hostDefault.Id == "default", "no profiles and no settings: the host's default shell, exactly as phase T started it");

        Check.True(CraftSettings.Allows(SettingLayer.Workspace, "CRAFT_TERMINAL_DEFAULT_PROFILE") && !CraftSettings.Allows(SettingLayer.Workspace, "CRAFT_TERMINAL_PROFILES"),
            "a workspace may pick its default profile but not add profiles (what runs is code)");
        var shape = CraftOptions.Variables.Single(v => v.Name == "CRAFT_TERMINAL_DEFAULT_PROFILE").Shape;
        Check.True(shape is { Kind: SettingKind.Choice, Section: SettingSections.Terminal, ChoicesFrom: SettingChoiceSources.TerminalProfiles }, "the default profile is a drop-down of the profiles on the settings page (Terminal)");
        var design = CraftOptions.FromVariables(name => name switch
        {
            "CRAFT_TERMINAL_TABS" => "LIST",
            "CRAFT_TERMINAL_PADDING" => "12",
            "CRAFT_TERMINAL_CURSOR" => "bar",
            "CRAFT_TERMINAL_CURSOR_BLINK" => "false",
            "CRAFT_TERMINAL_LIST_FROM" => "4",
            _ => null,
        });
        Check.True(design is { TerminalTabs: "list", TerminalPadding: 12, TerminalCursor: "bar", TerminalCursorBlink: false, TerminalListFrom: 4 }, "the design's settings");
        Check.Throws<ArgumentException>(() => CraftOptions.FromVariables(n => n == "CRAFT_TERMINAL_CURSOR" ? "beam" : null), "an unknown cursor shape is refused, naming the variable");
        Check.True(new CraftOptions() is { TerminalTabs: "auto", TerminalListFrom: 3, TerminalPadding: 8, TerminalCursor: "block", TerminalCursorBlink: true }, "the defaults: auto from three, 8 units, a blinking block");
    }

    [Test]
    public static void ShortNamesListsCursorAndBusy()
    {
        Check.Equal("ping", TerminalLaunch.ShortTitle(@"C:\WINDOWS\system32\cmd.exe - ping  localhost", "cmd"), "cmd's title while a program runs gives the program");
        Check.Equal("pwsh", TerminalLaunch.ShortTitle(@"C:\Program Files\PowerShell\7\pwsh.exe", "x"), "a program's path gives its file name");
        Check.Equal("cmd", TerminalLaunch.ShortTitle(@"Administrator: C:\WINDOWS\system32\cmd.exe", "x"), "an administrator's window too");
        Check.Equal("me@box: ~/src", TerminalLaunch.ShortTitle("me@box: ~/src", "bash"), "a shell's own words stay");
        Check.Equal("bash", TerminalLaunch.ShortTitle("  ", "bash"), "no title: the profile's short name");
        Check.True(TerminalLaunch.ShortTitle(new string('x', 40), "y").Length == 28, "a long title is cut");

        var options = new CraftOptions();
        Check.True(!TerminalLaunch.ListsTerminals(options, 2) && TerminalLaunch.ListsTerminals(options, 3), "auto: tabs for two, a list from three");
        Check.True(TerminalLaunch.ListsTerminals(options with { TerminalTabs = "list" }, 1) && !TerminalLaunch.ListsTerminals(options with { TerminalTabs = "tabs" }, 9), "list and tabs as set");

        var screen = new TerminalScreen(20, 4, 10) { DefaultCursorStyle = TerminalCursorStyle.Bar };
        Check.Equal(TerminalCursorStyle.Bar, screen.CursorStyle, "CRAFT_TERMINAL_CURSOR sets the cursor until the shell asks");
        screen.Feed("\e[4 q"u8);
        Check.Equal(TerminalCursorStyle.Underline, screen.CursorStyle, "DECSCUSR 4 is the shell's choice");
        screen.DefaultCursorStyle = TerminalCursorStyle.Block;
        Check.Equal(TerminalCursorStyle.Underline, screen.CursorStyle, "the setting does not override the shell's choice");
        screen.Feed("\e[0 q"u8);
        Check.Equal(TerminalCursorStyle.Block, screen.CursorStyle, "DECSCUSR 0 goes back to the setting's shape");
        Check.Equal(TerminalCursorStyle.Underline, TerminalLaunch.CursorStyle("underline"), "the setting's names");

        Check.True(screen.CommandRunning is null, "a shell that marks nothing leaves the busy dot to the output");
        screen.Feed("\e]133;A\a\e]133;B\a\e]133;C\a"u8);
        Check.True(screen.CommandRunning == true, "OSC 133;C: a command runs");
        screen.Feed("\e]133;D;0\a"u8);
        Check.True(screen.CommandRunning == false, "OSC 133;D: it ended");
    }

    [Test]
    public static async Task TheManagerOpensProfilesSplitsRenamesAndListsThem()
    {
        using var folder = new TempFolder();
        var options = new CraftOptions { TerminalHost = TerminalHosts.Pipe, TerminalCursor = "underline" };
        using var host = new EditorHost(options);
        host.WorkspaceFolder = folder.Path;
        var shellProgram = TerminalHosts.Get(TerminalHosts.Pipe).DefaultShell().Program;
        TerminalProfiles.Register(new FixedSource("test-profiles", [new TerminalProfile("test-sh", "Test Shell", new TerminalShell(shellProgram)) { ShortName = "tsh", Icon = TerminalIcons.Bash }]));
        try
        {
            var ui = new PickingUi();
            string? stored = null;
            using var manager = new TerminalManager(host, () => options, new FakeClipboard())
            {
                HomeFolder = folder.Path,
                Ui = ui,
                StoreDefaultProfile = name =>
                {
                    stored = name;
                    options = options with { TerminalDefaultProfile = name };
                    return null;
                },
            };
            await manager.ProfilesReady;
            var profile = manager.Profiles.Single(p => p.Id == "test-sh");
            var newProfile = TerminalCommandIds.NewProfile("test-sh");
            Check.True(EditorCommands.Find(newProfile)?.Title == "Terminal: New Test Shell", "each profile has a palette command, \"Terminal: New (profile)\"");
            var rows = ShellMenus.Rows(EditorContextMenus.TerminalProfiles, host);
            Check.True(rows.Any(r => r.CommandId == newProfile && r.Title == "New Test Shell") && rows[^1] is { CommandId: TerminalCommandIds.SelectDefaultProfile, StartsGroup: true },
                "the chevron's menu: a row per profile, then Set default profile… after a line");
            Check.SequenceEqual([TerminalCommandIds.Copy, TerminalCommandIds.Paste, TerminalCommandIds.SelectAll, TerminalCommandIds.Clear, TerminalCommandIds.Split, TerminalCommandIds.Rename, TerminalCommandIds.Kill],
                [.. ShellMenus.Rows(EditorContextMenus.Terminal, host).Select(r => r.CommandId)], "the terminal's context menu: Copy, Paste, Select all, Clear, Split, Rename, Kill");

            var empty = ShellEmptyStates.Terminal(host, manager.Profiles, manager.DefaultProfile.Name, folder.Path, "the open folder");
            Check.True(empty is { Title: "No terminal open", ListTitle: "PROFILES", Icon: not null } && empty.Actions.Single() is { Primary: true, CommandId: TerminalCommandIds.New }
                && empty.Recent.Any(a => a.CommandId == newProfile), "the empty state: an icon, a primary New terminal, the profiles under it");

            Check.True(await host.ExecuteCommandAsync(newProfile), "New Test Shell runs");
            var first = manager.ActiveTerminal!;
            Check.True(first.Profile?.Id == "test-sh" && first.ShortTitle == "tsh" && first.Shell.Program == shellProgram, "the terminal runs the profile's program; its tab shows the profile's short name");
            lock (first.Screen.Sync)
            {
                Check.Equal(TerminalCursorStyle.Underline, first.Screen.CursorStyle, "the cursor setting reaches the new terminal");
            }

            var split = manager.Split(first)!;
            Check.True(split.Group == first.Group && manager.GroupOf(first).Count == 2 && ReferenceEquals(manager.ActiveTerminal, split) && split.Profile?.Id == "test-sh",
                "Split opens the same profile beside it, in its group");
            var third = manager.Open(profile);
            var tabs = manager.Rows();
            Check.True(tabs.Count == 2 && tabs[0].Terminals.Count == 2 && tabs[0].Label == "tsh | tsh" && tabs[1].Active && !tabs[0].Active, "a tab per group, the split's names joined");
            Check.True(!TerminalLaunch.ListsTerminals(options, tabs.Count), "two groups: tabs");
            manager.Split(third);
            Check.True(manager.Terminals.Count == 4 && manager.Rows().Count == 2, "a split adds no tab");

            ui.Answer = "build";
            EditorContextMenus.Target = new ContextMenuTarget(EditorContextMenus.Terminal, null, false, first);
            try
            {
                Check.True(await host.ExecuteCommandAsync(TerminalCommandIds.Rename), "Rename runs on the context menu's terminal");
            }
            finally
            {
                EditorContextMenus.Target = null;
            }

            Check.True(first.ShortTitle == "build" && first.Title == "build" && manager.Rows()[0].Label.StartsWith("build", StringComparison.Ordinal), "renamed, its tab shows the new name");
            first.Rename("");
            Check.Equal("tsh", first.ShortTitle, "an empty name gives the shell's back");

            ui.Pick = 0;
            var before = manager.Profiles.Count;
            Check.True(await host.ExecuteCommandAsync(TerminalCommandIds.SelectDefaultProfile) && stored == manager.Profiles[0].Name, "Set default profile writes the picked profile's name to the settings");
            Check.True(manager.DefaultProfile.Is(stored) && manager.Profiles.Count == before, "and it is the default from then on");

            first.SelectAll();
            Check.True(first.Selection is { IsEmpty: false }, "Select all selects the scrollback and the screen");
            var opened = manager.Terminals.Count;
            Check.True(await host.ExecuteCommandAsync(TerminalCommandIds.Kill), "Kill");
            Check.Equal(opened - 1, manager.Terminals.Count, "it closed one terminal");
            first.Write(Encoding.UTF8.GetBytes("exit 7\r"));
            var ended = await Task.WhenAny(first.Completion, Task.Delay(TimeSpan.FromSeconds(30)));
            Check.True(ended == first.Completion && first.ExitCode == 7, "the profile's shell ran through the pipe host and reported its exit code");
            manager.Dispose();
            Check.True(EditorCommands.Find(newProfile) is null && ShellMenus.Rows(EditorContextMenus.TerminalProfiles, host).Count == 0, "disposed, the profiles' commands and rows are taken back");
        }
        finally
        {
            TerminalProfiles.Unregister("test-profiles");
        }
    }

    [Test]
    public static void TheBusyDotFollowsTheOutputAfterAnEnter()
    {
        using var folder = new TempFolder();
        var time = new ManualTime();
        var session = new OutputTerminalSession();
        using var terminal = TerminalInstance.Output(1, session, new TerminalSize(40, 5), 10, static action => action(), "busy", folder.Path);
        terminal.Clock = time;
        var after = TimeSpan.FromMilliseconds(500);
        var quiet = TimeSpan.FromMilliseconds(400);
        Check.True(!terminal.IsBusy(time, time.GetTimestamp(), after, quiet), "an output tab is never busy");

        var shell = new ScriptedSession();
        using var running = TerminalInstanceFactory(shell, time);
        running.Write("make\r"u8);
        Check.True(!running.IsBusy(time, time.GetTimestamp(), after, quiet), "just sent: not busy yet");
        time.AdvanceMilliseconds(600);
        shell.Emit("compiling…\r\n");
        Check.True(SpinUntil(() => running.BytesRead > 0), "the output arrived");
        Check.True(running.IsBusy(time, time.GetTimestamp(), after, quiet), "a command that still writes after 500 ms is busy");
        time.AdvanceMilliseconds(450);
        Check.True(!running.IsBusy(time, time.GetTimestamp(), after, quiet), "quiet for 400 ms: the dot goes");
        shell.Emit("\e]133;C\a");
        Check.True(SpinUntil(() => { lock (running.Screen.Sync) { return running.Screen.CommandRunning == true; } }), "the mark arrived");
        Check.True(running.IsBusy(time, time.GetTimestamp(), after, quiet), "a shell's OSC 133 mark says it exactly, quiet or not");
    }

    private static TerminalInstance TerminalInstanceFactory(ScriptedSession session, ManualTime time)
    {
        var host = new ScriptedHost(session);
        var terminal = TerminalInstance.Start(9, host, new TerminalStartInfo(new TerminalShell("scripted"), Path.GetTempPath(), new TerminalSize(40, 5), new Dictionary<string, string>()), 10, static action => action());
        terminal.Clock = time;
        return terminal;
    }

    private static bool SpinUntil(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                return false;
            }

            Thread.Yield();
        }

        return true;
    }

    private sealed class FixedSource(string name, IReadOnlyList<TerminalProfile> profiles) : ITerminalProfileSource
    {
        public string Name { get; } = name;

        public string Summary => "test profiles";

        public IReadOnlyList<TerminalProfile> Find(CancellationToken cancellationToken = default) => profiles;
    }

    private sealed class ThrowingSource : ITerminalProfileSource
    {
        public string Name => "test-broken";

        public string Summary => "a source that fails";

        public IReadOnlyList<TerminalProfile> Find(CancellationToken cancellationToken = default) => throw new IOException("the disk is gone");
    }

    /// <summary>A host whose one session is driven by the test.</summary>
    private sealed class ScriptedHost(ScriptedSession session) : ITerminalHost
    {
        public string Name => "test-scripted";

        public string Summary => "a session the test writes";

        public TerminalShell DefaultShell() => new("scripted");

        public ITerminalSession Start(TerminalStartInfo start) => session;
    }

    /// <summary>A shell's session the test feeds output into.</summary>
    private sealed class ScriptedSession : ITerminalSession
    {
        private readonly System.Threading.Channels.Channel<byte[]> _output = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ProcessId => 0;

        public Task<int> Exited => _exited.Task;

        public void Emit(string text) => _output.Writer.TryWrite(Encoding.UTF8.GetBytes(text));

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!await _output.Reader.WaitToReadAsync(cancellationToken))
            {
                return 0;
            }

            var bytes = await _output.Reader.ReadAsync(cancellationToken);
            bytes.CopyTo(buffer);
            return bytes.Length;
        }

        public void Write(ReadOnlySpan<byte> input)
        {
        }

        public void Resize(TerminalSize size)
        {
        }

        public void Dispose()
        {
            _output.Writer.TryComplete();
            _exited.TrySetResult(0);
        }
    }

    /// <summary>A shell UI that answers prompts and picks with what the test set.</summary>
    private sealed class PickingUi : IShellUi
    {
        public int Pick { get; set; } = -1;

        public string? Answer { get; set; }

        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(UnsavedChoice.Cancel);

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => Task.FromResult(Pick);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<string?> PromptAsync(string title, string text, string placeholder, string initial = "", string ok = "OK", CancellationToken cancellationToken = default) =>
            Task.FromResult(Answer);
    }
}
