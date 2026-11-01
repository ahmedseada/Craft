// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Settings;
using Craft.Core.Workspaces;
using Craft.CSharp.Run;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Editor.Run;
using Craft.Editor.Search;
using Craft.Editor.Shell;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// X part 6, the Run view: the providers' targets for a fixture with a solution, two projects, launch settings and scripts;
/// a run through a registered (fake) sandbox after the user's approval, its output in its own terminal tab and its errors
/// in Problems; Stop ending a real process tree; <c>.craft/run.json</c> round trip and the configuration edited in the
/// view; F5, Ctrl+F5, Ctrl+Shift+B, Shift+F5 and Ctrl+Shift+F in both keymaps, and the rail's Search and Run.
/// </summary>
[TestGroup("RunView")]
public static class RunViewTests
{
    [Test]
    public static void TheProvidersFindTheSolutionProjectsProfilesAndScripts()
    {
        using var folder = new TempFolder();
        Fixture(folder.Path);
        var dotnet = new DotnetRunProvider();
        var files = WorkspaceFiles.Enumerate(folder.Path, ["bin", "obj"], true, null, null).Select(p => WorkspaceFiles.Relative(folder.Path, p)).ToList();
        var workspace = new RunWorkspace(folder.Path, files);
        var targets = dotnet.Find(workspace);
        string Ids(IEnumerable<RunTarget> list) => string.Join(" | ", list.Select(t => $"{t.Id} [{t.Actions}]"));
        Check.Equal("src/App/App.csproj [Run, Build, Publish] | src/App/App.csproj#App [Run] | src/App/App.csproj#Tool [Run] | App.sln [Build, Test] | src/Lib/Lib.csproj [Build] | tests/App.Tests/App.Tests.csproj [Build, Test]",
            Ids(targets), "an Exe runs, builds and publishes; its two launch profiles run (IIS Express is left out); the solution builds and tests; the library builds; the test project tests");
        var profile = targets.Single(t => t.Id.EndsWith("#App", StringComparison.Ordinal));
        Check.True(profile is { Url: "https://localhost:5001", Arguments: "--verbose" } && profile.Environment["ASPNETCORE_ENVIRONMENT"] == "Development", "a profile's address, arguments and variables");
        Check.True(targets[0].Detail == "Exe · net10.0" && targets.Single(t => t.Id.Contains("Lib", StringComparison.Ordinal)).Detail == "Library · net10.0, netstandard2.0", "each project's kind and frameworks");
        Check.True(targets[0].ReadsInput && profile.ReadsInput && !targets.Single(t => t.Id.EndsWith("#Tool", StringComparison.Ordinal)).ReadsInput
            && !targets.Single(t => t.Id.Contains("Lib", StringComparison.Ordinal)).ReadsInput && !targets.Single(t => t.Id == "App.sln").ReadsInput,
            "a console project and its Project profile read input (X part 6b); an Executable profile, a library and a solution do not");
        Check.True(!DotnetRunProvider.ReadProject(Path.Combine(folder.Path, "tests", "App.Tests", "App.Tests.csproj")).Console, "a test project is not a console program");
        Check.True(dotnet.Watches("App.csproj") && dotnet.Watches("launchSettings.json") && dotnet.Watches("x.slnx") && !dotnet.Watches("Program.cs"), "what it watches");

        var scripts = RunProviders.Get(RunProviders.Scripts).Find(workspace);
        Check.Equal("build.ps1 | run.cmd | scripts/deploy.sh", string.Join(" | ", scripts.Select(t => t.Id)), "scripts at the root and in scripts/ (not deeper, not elsewhere)");

        var run = dotnet.Command(targets[0], RunActions.Run, new RunConfiguration("App", "dotnet", targets[0].Id) { Arguments = "--port 5000 \"a b\"" }, folder.Path);
        if (run.Problem is null)
        {
            Check.True(run.Command.Skip(1).SequenceEqual(["run", "--project", Path.Combine(folder.Path, "src", "App", "App.csproj"), "--", "--port", "5000", "a b"]), "dotnet run --project, the arguments after --: " + run.Line);
            Check.Equal(Path.Combine(folder.Path, "src", "App"), run.Folder, "in the project's folder");
            var withProfile = dotnet.Command(profile, RunActions.Run, null, folder.Path);
            Check.True(withProfile.Command.Contains("--launch-profile") && withProfile.Command[^1] == "--verbose" && withProfile.Environment.ContainsKey("ASPNETCORE_ENVIRONMENT"), "a profile through --launch-profile, its arguments and variables: " + withProfile.Line);
            var build = dotnet.Command(targets.Single(t => t.Id == "App.sln"), RunActions.Build, null, folder.Path);
            Check.True(build.Command.Skip(1).SequenceEqual(["build", Path.Combine(folder.Path, "App.sln"), "-nologo"]), "the solution builds: " + build.Line);
        }
        else
        {
            Check.True(run.Problem.Contains("dotnet", StringComparison.Ordinal), "no .NET SDK here: said, nothing runs");
        }

        var tool = dotnet.Command(targets.Single(t => t.Id.EndsWith("#Tool", StringComparison.Ordinal)), RunActions.Run, null, folder.Path);
        Check.True(tool.Command is [var program, "--fast"] && program.EndsWith("tool.exe", StringComparison.Ordinal), "an Executable profile runs its program: " + tool.Line);
        Check.True(dotnet.Command(targets[0], RunActions.Test, null, folder.Path).Problem is { } cannot && cannot.Contains("cannot test", StringComparison.Ordinal), "an action the target lacks is refused, said");
        Check.Equal(OperatingSystem.IsWindows() ? "net10.0-windows10.0.19041.0" : "net10.0", DotnetRunProvider.FrameworkFor(["net10.0-android", "net10.0-windows10.0.19041.0", "net10.0"]),
            "a multi-targeted project runs this system's framework (Windows'), else the one for no system");
    }

    [Test]
    public static async Task ARunAsksFirstGoesThroughTheSandboxAndItsErrorsGoToProblems()
    {
        using var folder = new TempFolder();
        Fixture(folder.Path);
        var program = Path.Combine(folder.Path, "src", "App", "Program.cs");
        var sandbox = new FakeSandbox
        {
            Lines =
            [
                "  Determining projects to restore...",
                $"{program}(3,9): error CS1002: ; expected [{Path.Combine(folder.Path, "src", "App", "App.csproj")}]",
                $"{program}(3,9): error CS1002: ; expected [{Path.Combine(folder.Path, "src", "App", "App.csproj")}]",
                "Program.cs(1,1,1,5): warning CS0168: The variable 'x' is declared but never used",
                "CSC : error CS2001: Source file 'gone.cs' could not be found.",
            ],
            ExitCode = 1,
        };
        CommandSandboxes.Register(sandbox);
        RunProviders.Register(new DotnetRunProvider());
        try
        {
            var ui = new ConcurrentQueue<Action>();
            var presenter = new AskingPresenter();
            var options = new CraftOptions { RunSandbox = sandbox.Name, WorkspaceWatcher = FolderWatchers.None };
            using var host = new EditorHost(options, ui.Enqueue, presenter);
            var terminals = new FakeTerminals();
            host.Terminals = terminals;
            using var view = new RunView(host, () => folder.Path, () => options);
            await Refreshed(ui, view);
            var configuration = new RunConfiguration("App", DotnetRunProvider.ProviderName, "src/App/App.csproj") { Action = RunActions.Build, Environment = new Dictionary<string, string> { ["A"] = "1" } };

            presenter.Answers.Enqueue(1);                               // Cancel
            var refused = await Run(ui, view.RunAsync(configuration));
            Check.True(refused.End == RunEnd.NotStarted && sandbox.Commands.Count == 0, "the user said no: nothing ran");
            Check.True(presenter.Asked is [{ Title: "Build App?" } asked] && asked.Details.Any(d => d.Contains(sandbox.Name, StringComparison.Ordinal)), "the question names the command, its folder, variables and sandbox");

            presenter.Answers.Enqueue(0);                               // Run
            var outcome = await Run(ui, view.RunAsync(configuration));
            if (outcome.Reason?.Contains("dotnet", StringComparison.Ordinal) == true)
            {
                return;                                                // no .NET SDK on this machine: the command is said, not run (checked above)
            }

            Check.True(outcome is { End: RunEnd.Failed, ExitCode: 1 }, $"exit code 1 is a failed build ({outcome.End}, {outcome.Reason})");
            Check.True(sandbox.Commands is [{ Folder: var ran, Limits: var limits }] && ran == Path.Combine(folder.Path, "src", "App") && limits.Environment!["A"] == "1"
                && limits.Time == Timeout.InfiniteTimeSpan && limits.Root == folder.Path, "through the registered sandbox, in its folder, with its variables, no time limit by default");
            Check.True(outcome.Problems.Count == 2 && outcome.Problems[0] is { Line: 3, Column: 9, Severity: DiagnosticSeverity.Error, Code: "CS1002", Message: "; expected" }
                && outcome.Problems[1] is { Severity: DiagnosticSeverity.Warning, File: var relative } && relative == Path.Combine(folder.Path, "src", "App", "Program.cs"),
                "the errors (each once; a relative file from the run's folder; a tool's own line not one)");
            await WorkspaceSearchTests.PumpUntil(ui, () => view.ProblemFiles.Count == 1, "the problems reach Problems");
            var file = view.ProblemFiles[0];
            Check.True(file.Diagnostics.Count == 2 && file.Snapshot.GetLineIndex(file.Diagnostics[0].Start) == 2, "placed in the file's text at line 3");
            var tab = terminals.Outputs.Single();
            Check.True(tab.Name == "Build: App" && tab.Text.ToString().Contains("$ ", StringComparison.Ordinal) && tab.Text.ToString().Contains("Determining projects", StringComparison.Ordinal)
                && tab.Text.ToString().Contains("exit code 1", StringComparison.Ordinal), "its output in its own terminal tab, the command first and its end last");
            Check.True(host.ActivityCenter.Recent.Any(a => a.Title == "Build: App"), "an activity, kept with its outcome");

            var again = await Run(ui, view.RunAsync(configuration));
            Check.True(presenter.Asked.Count == 2 && sandbox.Commands.Count == 2 && again.End == RunEnd.Failed, "the same command again: not asked again this session (CRAFT_RUN_APPROVAL once)");
            var each = options with { RunApproval = "each" };
            using var view2 = new RunView(host, () => folder.Path, () => each);
            await Refreshed(ui, view2);
            presenter.Answers.Enqueue(0);
            await Run(ui, view2.RunAsync(configuration));
            Check.Equal(3, presenter.Asked.Count, "with CRAFT_RUN_APPROVAL each, every run asks");

            var noSandbox = options with { RunSandbox = "nowhere" };
            using var view3 = new RunView(host, () => folder.Path, () => noSandbox);
            await Refreshed(ui, view3);
            presenter.Answers.Enqueue(0);
            var missing = await Run(ui, view3.RunAsync(configuration));
            Check.True(missing.End == RunEnd.NotStarted && missing.Reason!.Contains("not registered", StringComparison.Ordinal), "a sandbox that is not registered: said, nothing runs");
        }
        finally
        {
            CommandSandboxes.Unregister(sandbox.Name);
            RunProviders.Unregister(DotnetRunProvider.ProviderName);
        }

        Check.True(RunProblems.TryParse(@"C:\src\A.cs(10,2): error CS0103: The name 'x' does not exist in the current context [C:\src\A.csproj]", OperatingSystem.IsWindows() ? @"C:\src" : "/src", out var windows)
            && windows is { Line: 10, Column: 2, Code: "CS0103" } && windows.Message.EndsWith("context", StringComparison.Ordinal), "a Windows path's line read");
        Check.True(RunProblems.TryParse("tsc.json : error TS5023: Unknown option", "/w", out var noLine) && noLine is { Line: 1, Column: 1 }, "a file without a place: line 1");
        Check.True(!RunProblems.TryParse("Build succeeded.", "/w", out _) && !RunProblems.TryParse("  0 Error(s)", "/w", out _), "other lines are not problems");
    }

    [Test]
    public static async Task StopEndsARunningScriptWithItsProcessTree()
    {
        if (OperatingSystem.IsWindows())
        {
            return;                                                    // the owner's run covers Windows' cmd and PowerShell
        }

        using var folder = new TempFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "scripts"));
        var marker = Path.Combine(folder.Path, "child.pid");
        Disk.Write(Path.Combine(folder.Path, "scripts", "wait.sh"), $"echo started\nsleep 60 &\necho $! > '{marker}'\nwait\n");
        if (CommandSandboxes.Find(CommandSandboxes.Process) is null)
        {
            CommandSandboxes.Register(new Idrak.Text.Generation.ProcessCommandSandbox());
        }

        var ui = new ConcurrentQueue<Action>();
        var presenter = new AskingPresenter();
        var options = new CraftOptions { WorkspaceWatcher = FolderWatchers.None };
        using var host = new EditorHost(options, ui.Enqueue, presenter);
        var terminals = new FakeTerminals();
        host.Terminals = terminals;
        using var view = new RunView(host, () => folder.Path, () => options);
        await Refreshed(ui, view);
        var target = view.Targets.Single(t => t.Id == "scripts/wait.sh");
        presenter.Answers.Enqueue(0);
        var running = view.ActAsync("run.target", RunView.KeyOf(target));
        await WorkspaceSearchTests.PumpUntil(ui, () => terminals.Outputs.Any(o => o.Text.ToString().Contains("started", StringComparison.Ordinal)) && File.Exists(marker), "the script started");
        Check.True(view.Running.Count == 1 && view.Blocks.OfType<PanelResults>().Single().Groups.SelectMany(g => g.Rows).Any(r => r.Badge == "running"), "it shows as running");
        Check.True(view.Stop(), "Stop");
        await WorkspaceSearchTests.PumpUntil(ui, () => running.IsCompleted, "the run ends");
        await running;
        Check.True(view.Running.Count == 0 && terminals.Outputs[0].Text.ToString().Contains("stopped", StringComparison.Ordinal), "stopped, and said in its tab");
        int child = int.Parse(Disk.Read(marker).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        await Task.Delay(300);
        Check.True(!Directory.Exists("/proc/" + child) || Disk.Read($"/proc/{child}/stat").Contains(" Z ", StringComparison.Ordinal), $"its child process {child} ended with it (the process tree)");
    }

    [Test]
    public static async Task RunJsonRoundTripsAndTheViewEditsAConfiguration()
    {
        var file = new RunFile(
        [
            new RunConfiguration("App", "dotnet", "src/App/App.csproj") { Arguments = "--x \"y z\"", Environment = new Dictionary<string, string> { ["B"] = "2", ["A"] = "1" }, Folder = "src", BuildFirst = true },
            new RunConfiguration("Tests", "dotnet", "App.sln") { Action = RunActions.Test },
            new RunConfiguration("Ask", "scripts", "ask.sh") { Interactive = RunInteractive.On },
            new RunConfiguration("Quiet", "scripts", "ask.sh") { Interactive = RunInteractive.Off },
        ], "App");
        var back = RunFile.Parse(file.ToJson());
        Check.True(back.Equals(file) && back.Problems.Count == 0, "written and read back the same: " + Encoding.UTF8.GetString(file.ToJson()));
        var broken = RunFile.Parse("""{ "configurations": [ { "name": "x" }, { "name": "y", "provider": "dotnet", "target": "a", "action": "fly" }, { "name": "z", "provider": "p", "target": "t" }, { "name": "w", "provider": "p", "target": "t", "interactive": "sometimes" } ], }"""u8);
        Check.True(broken.Configurations is [{ Name: "z", Interactive: RunInteractive.Auto }] && broken.Problems.Count == 3, "an entry without a target, with an unknown action or an unknown interactive is passed over and said");
        Check.True(Encoding.UTF8.GetString(file.ToJson()).Contains("\"interactive\": \"on\"", StringComparison.Ordinal) && !Encoding.UTF8.GetString(file.ToJson()).Contains("\"auto\"", StringComparison.Ordinal),
            "interactive is written when it is not auto");
        Check.True(RunFile.Parse("{ nope"u8).Problems is [var why] && why.Contains("does not parse", StringComparison.Ordinal), "a file that does not parse says why");

        using var folder = new TempFolder();
        Fixture(folder.Path);
        RunProviders.Register(new DotnetRunProvider());
        try
        {
            var ui = new ConcurrentQueue<Action>();
            var options = new CraftOptions { WorkspaceWatcher = FolderWatchers.System, WorkspaceWatchMilliseconds = 20 };
            using var host = new EditorHost(options, ui.Enqueue, new AskingPresenter());
            using var view = new RunView(host, () => folder.Path, () => options);
            view.WorkspaceChanged();
            await Refreshed(ui, view);
            Check.True(view.DefaultConfiguration(RunActions.Run) is { Name: "App", Target: "src/App/App.csproj" }, "no default: F5 takes the first project that runs");
            await view.ActAsync("run.add", "dotnet:src/App/App.csproj");
            Check.True(view.Blocks.OfType<PanelQuery>().Any(q => q.ActionId == "run.draft.name" && q.Value == "App"), "Add configuration opens its fields in the view");
            await view.ActAsync("run.draft.name", "App (port)");
            await view.ActAsync("run.draft.arguments", "--port 5000");
            await view.ActAsync("run.draft.environment", "A=1|B=two");
            await view.ActAsync("run.draft.buildFirst", "on");
            await view.ActAsync("run.draft.save", null);
            var path = RunFile.PathOf(folder.Path);
            await WorkspaceSearchTests.PumpUntil(ui, () => File.Exists(path), "the file is written");
            var saved = RunFile.Load(folder.Path);
            Check.True(saved.Configurations is [{ Name: "App (port)", Arguments: "--port 5000", BuildFirst: true } one] && one.Environment["B"] == "two" && saved.Default == "App (port)",
                ".craft/run.json holds it, the first configuration the default");
            Check.True(view.DefaultConfiguration(RunActions.Run)?.Name == "App (port)", "F5 runs the default");
            await view.ActAsync("run.makeDefault", "dotnet:App.sln");
            await WorkspaceSearchTests.PumpUntil(ui, () => RunFile.Load(folder.Path).Default == "dotnet:App.sln", "a target made the default");
            Check.True(view.DefaultConfiguration(RunActions.Build) is { Target: "App.sln", Action: RunActions.Build }, "Ctrl+Shift+B builds the solution, the default");

            // The folder watcher: a new project appears in the view.
            Disk.Write(Path.Combine(folder.Path, "src", "Lib", "New.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");
            await WorkspaceSearchTests.PumpUntil(ui, () => view.Targets.Any(t => t.Id == "src/Lib/New.csproj"), "the watcher finds the new project");
            await view.ActAsync("run.edit", "App (port)");
            await view.ActAsync("run.draft.delete", null);
            await WorkspaceSearchTests.PumpUntil(ui, () => RunFile.Load(folder.Path).Configurations.Count == 0, "Delete takes it out of the file");
        }
        finally
        {
            RunProviders.Unregister(DotnetRunProvider.ProviderName);
        }
    }

    [Test]
    public static async Task TheKeysAndTheRailInBothKeymaps()
    {
        using var folder = new TempFolder();
        var settings = new CraftSettings(folder.Isolated(), SettingsFile.Load(folder.File("data/settings.json")));
        var recent = RecentList.Load(folder.File("data/recent.json"), 10);
        using var host = new EditorHost();
        using var shell = new ShellModel(host, settings, recent, new NoDialogs(), new NoUi());
        foreach (var keyMap in new[] { "", StandardShortcuts.VisualStudio })
        {
            host.SetKeyBindings([], keyMap);
            string name = keyMap.Length == 0 ? "VS Code" : "Visual Studio";
            Check.Equal("F5", host.KeysOf(RunCommands.Run), $"{name}: F5 runs");
            Check.Equal("Ctrl+F5", host.KeysOf(RunCommands.RunWithoutDebugging), $"{name}: Ctrl+F5 runs without debugging");
            Check.Equal("Ctrl+Shift+B", host.KeysOf(RunCommands.Build), $"{name}: Ctrl+Shift+B builds");
            Check.Equal("Shift+F5", host.KeysOf(RunCommands.Stop), $"{name}: Shift+F5 stops");
            Check.Equal("Ctrl+Shift+F", host.KeysOf(SearchCommands.Show), $"{name}: Ctrl+Shift+F finds in files");
        }

        var rail = EditorRailItems.All;
        Check.True(rail.Any(r => r is { Id: WorkspaceSearchView.PanelId, CommandId: "craft.search.rail" }) && rail.Any(r => r is { Id: RunView.PanelId, Icon: RailIcons.Run }),
            "the rail's Search opens the Search view (not quick open); Run has its own item");
        Check.True(host.Panels.Any(p => p.Id == WorkspaceSearchView.PanelId) && host.Panels.Any(p => p.Id == RunView.PanelId), "both are side views");
        Check.True(await host.ExecuteCommandAsync(SearchCommands.Show) && shell.Panels.SideView == WorkspaceSearchView.PanelId, "Ctrl+Shift+F shows the Search view");
        Check.True(shell.Search.Blocks.OfType<PanelQuery>().First().Focus > 0, "with the keyboard in its query");
        Check.True(await host.ExecuteCommandAsync("craft.search.rail") && !shell.Panels.IsOpen(ShellPanel.Explorer), "the rail's Search again folds the side");
        Check.True(await host.ExecuteCommandAsync(ShellModel.SideViewCommand(RunView.PanelId)) && shell.Panels.SideView == RunView.PanelId, "the rail's Run shows the Run view");
        Check.True(!EditorCommands.Get(RunCommands.Stop).CanExecute(host), "Stop is off with nothing running");

        var workspace = SettingsFile.Parse(folder.File("ws.json"), """{ "CRAFT_RUN_SANDBOX": "mine", "CRAFT_RUN_APPROVAL": "each", "CRAFT_SEARCH_MAX_RESULTS": "10" }"""u8);
        var layered = new CraftSettings(_ => null, null, workspace);
        Check.True(layered.Read("CRAFT_RUN_SANDBOX") is null && layered.Read("CRAFT_RUN_APPROVAL") is null && layered.Read("CRAFT_SEARCH_MAX_RESULTS") == "10",
            "a workspace cannot say where or how its commands run (what runs is code); it may narrow its search");
    }

    [Test]
    public static async Task AConsoleProgramReadsTwoLinesTypedIntoItsTabAndStopEndsIt()
    {
        using var folder = new TempFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "scripts"));
        bool windows = OperatingSystem.IsWindows();
        string extension = windows ? ".cmd" : ".sh";
        if (windows)
        {
            Disk.Write(Path.Combine(folder.Path, "scripts", "ask.cmd"), "@echo off\r\necho Two lines, please?\r\nset /p FIRST=\r\nset /p SECOND=\r\necho got %FIRST%\r\necho got %SECOND%\r\n");
            Disk.Write(Path.Combine(folder.Path, "scripts", "wait.cmd"), "@echo off\r\necho waiting\r\nset /p X=\r\necho never\r\n");
        }
        else
        {
            Disk.Write(Path.Combine(folder.Path, "scripts", "ask.sh"), "echo 'Two lines, please?'\nread first\nread second\necho \"got $first\"\necho \"got $second\"\n");
            Disk.Write(Path.Combine(folder.Path, "scripts", "wait.sh"), "echo waiting\nread x\necho never\n");
        }

        if (CommandSandboxes.Find(CommandSandboxes.Process) is null)
        {
            CommandSandboxes.Register(new Idrak.Text.Generation.ProcessCommandSandbox());
        }

        var ui = new ConcurrentQueue<Action>();
        var presenter = new AskingPresenter();
        var options = new CraftOptions { WorkspaceWatcher = FolderWatchers.None };
        using var host = new EditorHost(options, ui.Enqueue, presenter);
        using var manager = new Craft.Editor.Terminal.TerminalManager(host, () => options, new Craft.Tests.Editor.FakeClipboard());
        host.Terminals = manager;
        using var view = new RunView(host, () => folder.Path, () => options);
        await Refreshed(ui, view);
        var ask = view.Targets.Single(t => t.Id == "scripts/ask" + extension);
        var wait = view.Targets.Single(t => t.Id == "scripts/wait" + extension);
        var auto = new RunConfiguration(ask.Title, ask.Provider, ask.Id);
        Check.True(ask.ReadsInput && RunView.TakesKeyboard(auto, ask, options), "a script reads input: its run takes the keyboard (CRAFT_RUN_INTERACTIVE auto)");
        Check.True(!RunView.TakesKeyboard(auto, ask, options with { RunInteractive = "off" }) && !RunView.TakesKeyboard(auto with { Interactive = RunInteractive.Off }, ask, options)
            && RunView.TakesKeyboard(auto with { Interactive = RunInteractive.On }, ask, options with { RunInteractive = "off" }), "the configuration's own interactive wins over CRAFT_RUN_INTERACTIVE");

        // Two lines typed into its tab (one edited with Backspace) reach the program, which prints them back.
        presenter.Answers.Enqueue(0);
        var running = view.RunAsync(auto);
        Craft.Editor.Terminal.TerminalInstance? tab = null;
        await WorkspaceSearchTests.PumpUntil(ui, () => (tab = manager.Terminals.FirstOrDefault(t => t.IsProgram)) is not null && Text(tab).Contains("Two lines, please?", StringComparison.Ordinal),
            "the program asks in a tab of its own");
        Check.True(presenter.Asked is [var question] && question.Details.Any(d => d.StartsWith("Input: its terminal tab takes the keyboard", StringComparison.Ordinal)),
            "the approval says the run takes the keyboard");
        Check.True(tab!.Title == $"Run: {ask.Title}" && ReferenceEquals(manager.ActiveTerminal, tab), "its tab, named for the run, is the active one");
        manager.Input.OnText("Ada");
        manager.Input.OnKey(EditorKey.Enter, KeyModifiers.None);
        manager.Input.OnText("Bobx");
        manager.Input.OnKey(EditorKey.Backspace, KeyModifiers.None);
        manager.Input.OnKey(EditorKey.Enter, KeyModifiers.None);
        var outcome = await Run(ui, running);
        await WorkspaceSearchTests.PumpUntil(ui, () => Text(tab).Contains("exit code", StringComparison.Ordinal), "its end is shown");
        string text = Text(tab);
        Check.True(outcome is { End: RunEnd.Succeeded, ExitCode: 0 }, $"the program ends after its two lines ({outcome.End}, {outcome.Reason})");
        Check.True(text.Contains("got Ada", StringComparison.Ordinal) && text.Contains("got Bob", StringComparison.Ordinal) && !text.Contains("got Bobx", StringComparison.Ordinal)
            && text.Contains("exit code 0", StringComparison.Ordinal), $"the two lines typed came back, the edited one as edited, and the end is said: {text.Trim()}");
        Check.True(text.IndexOf("Ada", StringComparison.Ordinal) < text.IndexOf("got Ada", StringComparison.Ordinal), "what was typed is echoed in the tab before the program's answer");
        await WorkspaceSearchTests.PumpUntil(ui, () => tab.HasExited, "the tab shows as ended");

        // Stop ends a program waiting for its input; the next run of it replaces its ended tab and is not asked again.
        var waitConfiguration = new RunConfiguration(wait.Title, wait.Provider, wait.Id);
        presenter.Answers.Enqueue(0);
        var waiting = view.RunAsync(waitConfiguration);
        Craft.Editor.Terminal.TerminalInstance? waitTab = null;
        await WorkspaceSearchTests.PumpUntil(ui, () => (waitTab = manager.Terminals.FirstOrDefault(t => t.Title == $"Run: {wait.Title}")) is not null && Text(waitTab).Contains("waiting", StringComparison.Ordinal),
            "the waiting program started");
        Check.True(view.Stop(), "Stop");
        var stopped = await Run(ui, waiting);
        await WorkspaceSearchTests.PumpUntil(ui, () => Text(waitTab!).Contains("stopped", StringComparison.Ordinal), "the stop is said in its tab");
        Check.True(stopped.End == RunEnd.Stopped && !Text(waitTab!).Contains("never", StringComparison.Ordinal), $"Stop ends it, with its process tree ({stopped.End})");
        await WorkspaceSearchTests.PumpUntil(ui, () => waitTab!.HasExited, "its tab shows as ended");

        int asked = presenter.Asked.Count;
        var again = view.RunAsync(waitConfiguration);
        Craft.Editor.Terminal.TerminalInstance? againTab = null;
        await WorkspaceSearchTests.PumpUntil(ui, () => (againTab = manager.Terminals.FirstOrDefault(t => t.Title == $"Run: {wait.Title}" && !t.HasExited)) is not null
            && Text(againTab).Contains("waiting", StringComparison.Ordinal), "the program started again");
        Check.True(presenter.Asked.Count == asked && manager.Terminals.Count(t => t.Title == $"Run: {wait.Title}") == 1,
            "the same command line is not asked again this session (CRAFT_RUN_APPROVAL once), and its ended tab was replaced");
        manager.Input.OnKey(EditorKey.C, KeyModifiers.Control);
        var interrupted = await Run(ui, again);
        await WorkspaceSearchTests.PumpUntil(ui, () => Text(againTab!).Contains("stopped", StringComparison.Ordinal), "the stop is said");
        Check.True(interrupted.End == RunEnd.Stopped && Text(againTab!).Contains("^C", StringComparison.Ordinal), $"Ctrl+C in its tab ends the program ({interrupted.End})");
    }

    private static string Text(Craft.Editor.Terminal.TerminalInstance terminal)
    {
        lock (terminal.Screen.Sync)
        {
            return terminal.Screen.TextOf(terminal.Screen.FirstLine, 0, terminal.Screen.ScreenTop + terminal.Screen.Rows - 1, terminal.Screen.Columns);
        }
    }

    // ---- fixture and fakes ----

    private static void Fixture(string root)
    {
        void Write(string relative, string text)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        Write("App.sln", "Microsoft Visual Studio Solution File, Format Version 12.00\n");
        Write("src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        Write("src/App/Program.cs", "class Program\n{\n    static void Main() { }\n}\n");
        Write("src/App/Properties/launchSettings.json", """
            {
              // comments are allowed here
              "profiles": {
                "App": { "commandName": "Project", "commandLineArgs": "--verbose", "applicationUrl": "https://localhost:5001;http://localhost:5000",
                         "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" } },
                "Tool": { "commandName": "Executable", "executablePath": "bin/tool.exe", "commandLineArgs": "--fast" },
                "IIS": { "commandName": "IISExpress" },
              }
            }
            """);
        Write("src/Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks></PropertyGroup></Project>");
        Write("tests/App.Tests/App.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup>"
            + "<ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"17.0.0\" /></ItemGroup></Project>");
        Write("build.ps1", "Write-Host build");
        Write("run.cmd", "@echo run");
        Write("scripts/deploy.sh", "echo deploy");
        Write("scripts/nested/deeper.sh", "echo no");
        Write("docs/tool.ps1", "Write-Host no");
        Write("bin/Debug/Other.csproj", "<Project />");
    }

    private static async Task Refreshed(ConcurrentQueue<Action> ui, RunView view)
    {
        var refresh = view.RefreshAsync();
        await WorkspaceSearchTests.PumpUntil(ui, () => refresh.IsCompleted, "the targets are found");
    }

    private static async Task<RunOutcome> Run(ConcurrentQueue<Action> ui, Task<RunOutcome> run)
    {
        await WorkspaceSearchTests.PumpUntil(ui, () => run.IsCompleted, "the run ends");
        return await run;
    }

    private sealed record Ran(IReadOnlyList<string> Command, string Folder, CommandLimits Limits);

    private sealed class FakeSandbox : ICommandSandbox
    {
        public List<Ran> Commands { get; } = [];

        public IReadOnlyList<string> Lines { get; init; } = [];

        public int ExitCode { get; init; }

        public string Name => "fake-run";

        public string Summary => "records each command and plays its output";

        public string? Check(CommandLimits limits) => null;

        public ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
        {
            lock (Commands)
            {
                Commands.Add(new Ran(command, directory, limits));
            }

            foreach (var line in Lines)
            {
                limits.Output?.Invoke(line);
            }

            return ValueTask.FromResult(new CommandResult(ExitCode, false, string.Join('\n', Lines), TimeSpan.FromMilliseconds(5), false));
        }
    }

    private sealed class AskingPresenter : IEditorPresenter
    {
        public ConcurrentQueue<int> Answers { get; } = new();

        public List<EditorQuestion> Asked { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken)
        {
            Asked.Add(question);
            return Task.FromResult(Answers.TryDequeue(out int answer) ? answer : question.Dismissed);
        }
    }

    private sealed class FakeTerminals : IEditorTerminals
    {
        public List<FakeOutput> Outputs { get; } = [];

        public IReadOnlyList<IEditorTerminal> All => [];

        public IEditorTerminal? Active => null;

        public IEditorTerminal Open(EditorTerminalRequest request) => throw new InvalidOperationException("no shells in this test");

        public IEditorTerminalOutput? OpenOutput(string name)
        {
            var output = Outputs.FirstOrDefault(o => o.Name == name);
            if (output is null)
            {
                output = new FakeOutput(name);
                Outputs.Add(output);
            }

            return output;
        }
    }

    private sealed class FakeOutput(string name) : IEditorTerminalOutput
    {
        public string Name { get; } = name;

        public StringBuilder Text { get; } = new();

        public string Title => Name;

        public bool IsOpen => true;

        public bool Shown { get; private set; }

        public bool Append(string text)
        {
            lock (Text)
            {
                Text.Append(text);
            }

            return true;
        }

        public void Show() => Shown = true;
    }

    private sealed class NoDialogs : IFileDialogs
    {
        public Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class NoUi : IShellUi
    {
        public Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(UnsavedChoice.Cancel);

        public Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default) => Task.FromResult(-1);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
    }
}
