// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Loader;
using System.Text;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Plugins;
using Craft.Core.Settings;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Craft.Plugins.Npm;
using Craft.Plugins.Sandbox;
using Craft.Tests.Runner;
using Idrak.Abstraction.Plugins;

namespace Craft.Tests;

/// <summary>
/// Phase W, W-npm: the npm plug-in (plugins/Craft.Plugins.Npm), pre-installed and removable. package.json read as bytes;
/// Node found on a fake machine; a workspace refused for the Node path (PluginSetting.MachineOnly); the scripts as commands,
/// menu rows and Run view targets following the file (the folder watcher, IEditorHost.WorkspaceChanged); every run through
/// the sandbox, approve-each's question naming the npm plug-in and the script's text (Idrak's CommandLimits.Requester and
/// Purpose); Cancel stopping the run; install as an activity; yarn, pnpm and bun said. The real-Node tests need Node on
/// PATH; without it they say so and end.
/// </summary>
[TestGroup("WebNpm")]
public static class WebNpmTests
{
    private static readonly string NodeName = OperatingSystem.IsWindows() ? "node.exe" : "node";

    // ---- package.json as bytes ----

    [Test]
    public static void ScriptsParseFromBytesWithABomCommentsAndOddNames()
    {
        var json = "﻿{\n  // an editor's comment\n  \"name\": \"web-app\",\n  \"version\": \"1.0.0\",\n  \"config\": { \"scripts\": { \"nested\": \"not a script\" } },\n"
                   + "  \"scripts\": {\n    \"build\": \"ng build\",\n    \"build:prod\": \"ng build --configuration production\",\n    \"with space\": \"echo \\\"quoted\\\"\",\n"
                   + "    \"ünïcode\": \"echo ü\",\n    \"esc\\u0061ped\": \"echo a\",\n    \"\": \"no name\",\n    \"number\": 3,\n    \"object\": { \"x\": [1, 2] },\n"
                   + "    \"build\": \"ng build --watch=false\",\n  },\n  \"devDependencies\": { \"typescript\": \"~5.9\" },\n}\n";
        var package = NpmPackages.Parse(Encoding.UTF8.GetBytes(json), "web/package.json");
        Check.True(package.Problem is null, $"a BOM, a comment and trailing commas are taken ({package.Problem})");
        Check.Equal("web-app", package.Name, "its name");
        Check.SequenceEqual(["build", "build:prod", "with space", "ünïcode", "escaped"], [.. package.Scripts.Select(s => s.Name)],
            "every string script in its order, odd names kept, escapes read, no empty name, no number or object, nothing from a nested \"scripts\"");
        Check.Equal("ng build --watch=false", package.Script("build")!.Text, "a name written twice keeps its last text in its first place (JSON.parse)");
        Check.Equal("echo \"quoted\"", package.Script("with space")!.Text, "a script's text unescaped");
        Check.Equal("web", package.Folder, "its folder");

        Check.True(NpmPackages.Parse("[1, 2]"u8, "package.json") is { Problem: not null, Scripts.Count: 0 }, "an array is not a package.json");
        Check.True(NpmPackages.Parse("{ \"scripts\": { \"a\": \"x\" "u8, "package.json") is { Problem: { } broken } && broken.Contains("not valid JSON", StringComparison.Ordinal),
            "a broken file says so");
        Check.True(NpmPackages.Parse(""u8, "package.json").Problem is not null, "an empty file says so");
        var pnpm = NpmPackages.Parse("{\"packageManager\":\"pnpm@9.12.0+sha512.abc\",\"scripts\":{\"dev\":\"vite\"}}"u8, "package.json");
        Check.True(pnpm is { Manager: "pnpm", ManagerEvidence: "packageManager: pnpm@9.12.0+sha512.abc" }, $"packageManager names pnpm ({pnpm.Manager})");
        Check.True(NpmPackages.Parse("{\"packageManager\":\"npm@10.9.0\"}"u8, "package.json").Manager is null, "npm's own packageManager is npm's");

        using var temp = new TempFolder();
        Write(temp.File("package.json"), "{\"scripts\":{\"root\":\"echo root\"}}");
        Write(temp.File("yarn-app/package.json"), "{\"scripts\":{\"y\":\"echo y\"}}");
        Write(temp.File("yarn-app/yarn.lock"), "# yarn lockfile v1\n");
        Write(temp.File("mono/pnpm-lock.yaml"), "lockfileVersion: '9.0'\n");
        Write(temp.File("mono/packages/a/package.json"), "{\"scripts\":{\"a\":\"echo a\"}}");
        Write(temp.File("locked/package.json"), "{}");
        Write(temp.File("locked/package-lock.json"), "{}");
        Write(temp.File("node_modules/left-pad/package.json"), "{\"scripts\":{\"never\":\"x\"}}");
        Write(temp.File("ignored/package.json"), "{}");
        Write(temp.File(".gitignore"), "ignored/\n");
        Write(temp.File("dist/package.json"), "{}");
        var files = NpmPackages.Find(temp.Path, new NpmSettings().Ignore);
        Check.SequenceEqual(["locked/package.json", "mono/packages/a/package.json", "package.json", "yarn-app/package.json"], [.. files],
            "every package.json, in path order: node_modules never walked, the ignored folders and .gitignore's left out");
        Check.True(NpmPackages.Read(temp.Path, "yarn-app/package.json") is { Manager: "yarn", ManagerEvidence: "yarn-app/yarn.lock" }, "yarn by its lock file");
        Check.True(NpmPackages.Read(temp.Path, "mono/packages/a/package.json") is { Manager: "pnpm", ManagerEvidence: "mono/pnpm-lock.yaml" }, "pnpm by a lock file above, within the workspace");
        Check.True(NpmPackages.Read(temp.Path, "locked/package.json") is { Manager: null, HasNpmLock: true }, "npm's lock: npm, and ci can run");
        Check.True(NpmPackages.Read(temp.Path, "package.json") is { Manager: null, HasNpmLock: false }, "the root: npm, no lock");
        Check.True(NpmPackages.Matters("package.json") && NpmPackages.Matters("bun.lockb") && NpmPackages.Matters("package-lock.json") && !NpmPackages.Matters("tsconfig.json"),
            "the files that change what the plug-in found");
    }

    // ---- Node on a fake machine ----

    [Test]
    public static void NodeIsFoundOnAFakePathAndNpmBesideItInEachLayout()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);       // Windows' names ignore case ("node.EXE")
        bool Exists(string path) => files.Contains(Path.GetFullPath(path));
        string Env(string name, string? path) => name == "PATH" ? path! : null!;
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "npm-fake-machine"));
        bool windows = OperatingSystem.IsWindows();

        // A Unix layout found from PATH; a Unix PATH splits on ':', which a Windows path's drive letter holds ("D:\..."), so on
        // Windows the same layouts are found from the Node setting (a rooted path, no PATH read) and the rest is the same.
        NodeInstall UnixAt(string binFolder, Func<string, string?> link) => windows
            ? NodeInstall.Find(new NpmSettings { Node = Path.Combine(binFolder, "node") }, _ => null, windows: false, Exists, link)
            : NodeInstall.Find(new NpmSettings(), n => Env(n, binFolder), windows: false, Exists, link);

        // Windows' installer: node.exe with node_modules\npm beside it.
        string winFolder = Path.Combine(root, "nodejs");
        files.Add(Path.Combine(winFolder, "node.exe"));
        files.Add(Path.Combine(winFolder, "node_modules", "npm", "bin", "npm-cli.js"));
        var onPath = NodeInstall.Find(new NpmSettings(), n => Env(n, Path.Combine(root, "elsewhere") + ";" + winFolder), windows: true, Exists, _ => null);
        Check.True(onPath is { Ready: true } && string.Equals(onPath.Node, Path.Combine(winFolder, "node.exe"), StringComparison.OrdinalIgnoreCase) && onPath.NpmCli == Path.Combine(winFolder, "node_modules", "npm", "bin", "npm-cli.js"),
            $"Windows: node.exe on PATH, npm-cli.js beside it ({onPath.Node}, {onPath.NpmCli}, {onPath.Problem})");

        // Unix's tarball: bin/node and lib/node_modules/npm.
        string unix = Path.Combine(root, "node22");
        files.Add(Path.Combine(unix, "bin", "node"));
        files.Add(Path.Combine(unix, "lib", "node_modules", "npm", "bin", "npm-cli.js"));
        var tarball = UnixAt(Path.Combine(unix, "bin"), _ => null);
        Check.True(tarball is { Ready: true } && tarball.NpmCli == Path.Combine(unix, "lib", "node_modules", "npm", "bin", "npm-cli.js"), $"Unix: ../lib/node_modules/npm ({tarball.NpmCli})");

        // A link (Homebrew, nvm): bin/node links into the Cellar, whose ../lib holds npm.
        string brew = Path.Combine(root, "brew");
        string cellar = Path.Combine(brew, "Cellar", "node", "23.1.0");
        files.Add(Path.Combine(brew, "bin", "node"));
        files.Add(Path.Combine(cellar, "lib", "node_modules", "npm", "bin", "npm-cli.js"));
        var linked = UnixAt(Path.Combine(brew, "bin"),
            p => p == Path.Combine(brew, "bin", "node") ? Path.Combine(cellar, "bin", "node") : null);
        Check.True(linked is { Ready: true } && linked.NpmCli == Path.Combine(cellar, "lib", "node_modules", "npm", "bin", "npm-cli.js"), $"through Node's link ({linked.NpmCli})");

        // The settings: Node at a path; npm as its package's folder; a Node that is not there names the variable.
        var given = NodeInstall.Find(new NpmSettings { Node = Path.Combine(unix, "bin", "node"), Npm = Path.Combine(unix, "lib", "node_modules", "npm") }, _ => null, windows: false, Exists, _ => null);
        Check.True(given is { Ready: true } && given.NpmCli!.EndsWith("npm-cli.js", StringComparison.Ordinal), $"CRAFT_PLUGIN_NPM_NODE and _NPM ({given.Problem})");
        var wrong = NodeInstall.Find(new NpmSettings { Node = Path.Combine(root, "nowhere", "node") }, _ => null, windows: false, Exists, _ => null);
        Check.True(wrong is { Ready: false, Problem: { } why } && why.Contains("CRAFT_PLUGIN_NPM_NODE", StringComparison.Ordinal), $"a Node path that is not there names its variable ({wrong.Problem})");
        var none = NodeInstall.Find(new NpmSettings(), n => Env(n, Path.Combine(root, "empty")), windows, Exists, _ => null);
        Check.True(none is { Ready: false, Problem: { } missing } && missing.Contains("not found on PATH", StringComparison.Ordinal) && missing.Contains("CRAFT_PLUGIN_NPM_NODE", StringComparison.Ordinal),
            $"no Node on PATH: said, with the setting to use ({none.Problem})");
        files.Add(Path.Combine(root, "lonely", "bin", "node"));
        var noNpm = UnixAt(Path.Combine(root, "lonely", "bin"), _ => null);
        Check.True(noNpm is { Ready: false, Node: not null, Problem: { } cli } && cli.Contains("CRAFT_PLUGIN_NPM_NPM", StringComparison.Ordinal), "Node without npm beside it names npm's setting");

        Check.Equal("22.23.4", NodeInstall.ParseVersion("v22.23.4\n"), "node --version's output");
        Check.Equal("24.0.0-nightly2026", NodeInstall.ParseVersion("v24.0.0-nightly2026\r\n"), "a pre-release");
        Check.True(NodeInstall.ParseVersion("") is null && NodeInstall.ParseVersion("node: not found") is null, "no version in other output");
        Check.Equal("Node 22.23.4", new NodeInstall("n", "c", null) { Version = "22.23.4" }.StatusText, "the status item's text");

        var settings = new NpmSettings();
        Check.True(settings.IsLongRunning("start") && settings.IsLongRunning("dev:server") && settings.IsLongRunning("Watch") && !settings.IsLongRunning("develop") && !settings.IsLongRunning("build"),
            "long-running: the names and their ':' variants");
        Check.True(settings.TimeLimitOf("dev") == Timeout.InfiniteTimeSpan && settings.TimeLimitOf("build") == TimeSpan.FromMinutes(30) && settings.TimeLimitOf(null) == TimeSpan.FromMinutes(30)
            && (settings with { TimeLimitMinutes = 0 }).TimeLimitOf("build") == Timeout.InfiniteTimeSpan, "no limit for a long-running script, 30 minutes for the rest, 0 for none");
        var environment = NpmRuns.Environment(settings);
        Check.True(environment["npm_config_fund"] == "false" && environment["npm_config_audit"] == "false" && environment["NG_CLI_ANALYTICS"] == "false" && environment["FORCE_COLOR"] == "1"
            && !NpmRuns.Environment(settings with { Color = false }).ContainsKey("FORCE_COLOR"), "the run's variables: no fund, audit or analytics questions, colours kept");
        var install = new NodeInstall("/n/node", "/n/npm-cli.js", null);
        Check.SequenceEqual(["/n/node", "/n/npm-cli.js", "run", "build", "--", "--port", "4300", "a b"], NpmRuns.Script(install, "build", "--port 4300 \"a b\""),
            "node npm-cli.js run SCRIPT -- arguments: never npm.cmd, never a shell of ours");
    }

    // ---- MachineOnly: a workspace cannot name the Node to run ----

    [Test]
    public static void AWorkspaceCannotSetTheNodePathAndThePageSaysSo()
    {
        const string Variable = "CRAFT_PLUGIN_NPM_NODE";
        Check.True(PluginSetting.IsVariable(Variable, "npm", "node") && !PluginSetting.IsVariable(Variable, "npm", "npm") && !PluginSetting.IsVariable("CRAFT_PLUGIN_NPM_NODEX", "npm", "node"),
            "a variable is compared with its plug-in and key in place");
        var package = new NpmPluginPackage();
        using var temp = new TempFolder();
        var user = SettingsFile.Load(temp.File("user/settings.json"));
        var workspace = SettingsFile.Load(temp.File("ws/.craft/settings.json"));
        workspace.Set(Variable, "/evil/node");
        workspace.Set("CRAFT_PLUGIN_NPM_INSTALL", "ci");
        user.Set(Variable, "/usr/local/bin/node");
        IdrakPlugins.RegisterAll(package);
        try
        {
            Check.True(EditorPlugins.IsMachineOnly(Variable) && EditorPlugins.IsMachineOnly("CRAFT_PLUGIN_NPM_SANDBOX") && !EditorPlugins.IsMachineOnly("CRAFT_PLUGIN_NPM_INSTALL"),
                "node, npm and sandbox are machine-only; install is not");
            var settings = new CraftSettings(_ => null, user, workspace);
            Check.Equal("/usr/local/bin/node", settings.Read(Variable), "the workspace's Node is passed over: the user's is read");
            Check.Equal(SettingSource.User, settings.SourceOf(Variable), "and said to come from the user's settings");
            Check.Equal("ci", settings.Read("CRAFT_PLUGIN_NPM_INSTALL"), "a workspace still sets what is not machine-only");
            Check.True(!CraftSettings.Allows(SettingLayer.Workspace, Variable) && CraftSettings.Allows(SettingLayer.User, Variable), "the workspace's file may not set it; the user's may");
            Check.Throws<ArgumentException>(() => settings.Set(SettingLayer.Workspace, Variable, "/other/node"), "writing it into the workspace's file");
            var row = settings.Rows([new CraftVariable(Variable, "", "npm: Node")]).Single(r => r.Variable.Name == Variable);
            Check.True(row.Note is { } note && note.Contains("This machine's only", StringComparison.Ordinal) && note.Contains("passed over", StringComparison.Ordinal),
                $"the settings page says so beside it, and that the workspace's value is passed over ({row.Note})");
            Check.True(NpmSettings.Declared.Where(s => s.MachineOnly).Select(s => s.Key).SequenceEqual(["node", "npm", "sandbox"]), "the declared machine-only settings");
        }
        finally
        {
            IdrakPlugins.UnregisterAll(package);
        }

        Check.True(!EditorPlugins.IsMachineOnly(Variable) && new CraftSettings(_ => null, user, workspace).Read(Variable) == "/evil/node",
            "with the plug-in gone its setting is an unknown variable again (no plug-in declares it)");
        Check.True(Craft.Plugins.Git.GitSettings.Declared.Single(s => s.Key == "path").MachineOnly && SandboxSettings.Declared.Single(s => s.Key == "inner").MachineOnly,
            "git's program and approve-each's inner sandbox are machine-only too");
    }

    // ---- the scripts follow the file ----

    [Test]
    public static void ScriptsBecomeCommandsMenuRowsAndRunTargetsAndFollowTheFile()
    {
        using var npm = new Fixture(root =>
        {
            Write(Path.Combine(root, "package.json"), "{\"name\":\"site\",\"scripts\":{\"build\":\"ng build\",\"start\":\"ng serve\",\"test\":\"ng test\"}}");
            Write(Path.Combine(root, "src", "app", "package.json"), "{\"scripts\":{\"build\":\"tsc -p .\"}}");
            Write(Path.Combine(root, "other.json"), "{}");
        });
        npm.Ready();
        var session = npm.Session;
        Check.True(session.Install.Ready && session.Status is { Text: var text } && text.StartsWith("Node", StringComparison.Ordinal), $"Node found, its status item ({session.Status?.Text})");
        Check.True(RunProviders.Find(NpmRunProvider.ProviderName) is not null, "the Run view's provider \"npm\" is registered once Node is found");
        Check.Equal("npm: run build", EditorCommands.Find(NpmPlugin.ScriptCommandId("package.json", "build"))?.Title, "the root's script in the palette");
        Check.Equal("npm: run build (src/app)", EditorCommands.Find(NpmPlugin.ScriptCommandId("src/app/package.json", "build"))?.Title, "another package's, with its folder");
        Check.Equal("npm: run start in a terminal", EditorCommands.Find(NpmPlugin.TerminalCommandId("package.json", "start"))?.Title, "each script in a terminal too");
        Check.Equal("npm: install (src/app)", EditorCommands.Find(NpmPlugin.InstallCommandId("src/app/package.json"))?.Title, "each package's install");

        // The tree's menu on a package.json (and only there), its scripts made as it opens.
        var target = new ContextMenuTarget(EditorContextMenus.Tree, Path.Combine(npm.Root, "package.json"));
        var other = new ContextMenuTarget(EditorContextMenus.Tree, Path.Combine(npm.Root, "other.json"));
        var rows = EditorMenus.ItemsOf(EditorContextMenus.Tree).Where(i => i.CommandId is NpmPlugin.ScriptsMenu or NpmPlugin.TerminalMenu or NpmPlugin.InstallHereCommand).ToList();
        Check.True(rows.Count == 3 && rows.All(r => r.Icon is not null && r.When!.Matches(target) && !r.When.Matches(other)), "Run npm script ▸, in a terminal ▸ and npm install, with icons, on package.json only");
        Check.True(EditorMenus.ItemsOf(EditorContextMenus.Tab).Count(i => i.CommandId == NpmPlugin.ScriptsMenu) == 1, "the tab's menu too");
        var scripts = EditorMenuSources.ItemsFor(NpmPlugin.ScriptsMenu, target);
        Check.SequenceEqual(["build", "start", "test"], [.. scripts.Select(s => s.Title!)], "the submenu's rows: the scripts in their order");
        Check.True(scripts.All(s => s.CommandId == NpmPlugin.RunScriptCommand && s.Argument == s.Title), "each row runs the script named by its argument");
        Check.True(EditorMenuSources.ItemsFor(NpmPlugin.ScriptsMenu, other).Count == 0, "no rows for another file");

        // The Run view's targets and commands.
        var targets = NpmPlugin.Provider.Find(new RunWorkspace(npm.Root, ["other.json", "package.json", "src/app/package.json"]));
        Check.SequenceEqual(["package.json#build", "package.json#start", "package.json#test", "src/app/package.json#build"], [.. targets.Select(t => t.Id)], "a target per script");
        var build = targets[0];
        Check.True(build.Actions == (RunActions.Run | RunActions.Build) && targets[2].Actions == (RunActions.Run | RunActions.Test) && build.Environment["FORCE_COLOR"] == "1",
            "build builds, test tests (F5, Ctrl+Shift+B), the run's variables on the target");
        var command = NpmPlugin.Provider.Command(targets[3], RunActions.Run, new RunConfiguration("App", "npm", targets[3].Id) { Arguments = "--watch" }, npm.Root);
        Check.True(command.Command.SequenceEqual([session.Install.Node!, session.Install.NpmCli!, "run", "build", "--", "--watch"]) && command.Folder == Path.Combine(npm.Root, "src", "app"),
            $"the Run view's command: node npm-cli.js run build -- args, in the package's folder ({command.Line} in {command.Folder})");
        Check.True(NpmPlugin.Provider.Watches("package.json") && !NpmPlugin.Provider.Watches("App.csproj"), "the Run view looks again when a package.json changes");

        // Edited and refreshed (WorkspaceChanged: Refreshed), saved (DocumentSaved), removed on disk (the folder watcher).
        Write(Path.Combine(npm.Root, "package.json"), "{\"scripts\":{\"build\":\"ng build\",\"lint\":\"ng lint\"}}");
        npm.Host.RefreshWorkspace();
        npm.Until(() => EditorCommands.Find(NpmPlugin.ScriptCommandId("package.json", "lint")) is not null, "a script added is found after Refresh (WorkspaceChanged)");
        Check.True(EditorCommands.Find(NpmPlugin.ScriptCommandId("package.json", "start")) is null, "a script removed goes");
        File.Delete(Path.Combine(npm.Root, "src", "app", "package.json"));
        npm.Until(() => EditorCommands.Find(NpmPlugin.ScriptCommandId("src/app/package.json", "build")) is null, "a package.json deleted on disk takes its commands (the folder watcher)");

        // Another folder opened, then none.
        using var second = new TempFolder();
        Write(second.File("package.json"), "{\"scripts\":{\"serve\":\"vite\"}}");
        int changes = 0;
        npm.Host.WorkspaceChanged += (_, e) => changes += e.Kind == WorkspaceChangeKind.Switched ? 1 : e.Kind == WorkspaceChangeKind.Closed ? 100 : 0;
        npm.Host.WorkspaceFolder = second.Path;
        npm.Until(() => EditorCommands.Find(NpmPlugin.ScriptCommandId("package.json", "serve")) is not null && EditorCommands.Find(NpmPlugin.ScriptCommandId("package.json", "lint")) is null,
            "another folder: its scripts, the old ones gone");
        npm.Host.WorkspaceFolder = null;
        npm.Until(() => !EditorCommands.Names.Any(n => n.StartsWith("npm.run:", StringComparison.Ordinal)), "no folder: no script commands");
        Check.Equal(101, changes, "WorkspaceChanged said Switched, then Closed");
    }

    [Test]
    public static void NoNodeSaysWhyAndAddsNothing()
    {
        using var npm = new Fixture(root => Write(Path.Combine(root, "package.json"), "{\"scripts\":{\"build\":\"x\"}}"), node: "nowhere");
        npm.Ready();
        string why = npm.Session.Install.Problem ?? "";
        Check.True(!npm.Session.Install.Ready && why.Contains("CRAFT_PLUGIN_NPM_NODE", StringComparison.Ordinal), $"why, naming the setting ({why})");
        Check.True(RunProviders.Find(NpmRunProvider.ProviderName) is null && !EditorCommands.Names.Any(n => n.StartsWith("npm.", StringComparison.Ordinal))
            && !EditorMenus.ItemsOf(EditorContextMenus.Tree).Any(i => i.CommandId == NpmPlugin.ScriptsMenu) && npm.Session.Status is null,
            "no provider, no command, no menu row, no status item");
        Check.True(npm.Host.Notifications.Entries.Any(e => e.Title == "npm" && e.Text == why && e.Actions.Count == 1), "said once in the notification centre, with Look for Node again");
    }

    // ---- runs through the sandbox ----

    [Test]
    public static void TheQuestionNamesTheNpmPluginAndTheScriptAndCancelStopsTheRun()
    {
        var inner = new FakeSandbox("webnpm-inner") { Lines = ["\e[32m> site@1.0.0 build\e[0m", "Application bundle generation complete."] };
        CommandSandboxes.Register(inner);
        try
        {
            using var npm = new Fixture(root =>
            {
                Write(Path.Combine(root, "package.json"), "{\"scripts\":{\"build\":\"ng build --configuration production\",\"dev\":\"vite --port 4300\"}}");
                Write(Path.Combine(root, ".npmrc"), "//registry.npmjs.org/:_authToken=npm_SECRET_TOKEN_42\n");
            }, approveEach: true, vars: new() { ["CRAFT_PLUGIN_SANDBOX_INNER"] = inner.Name });
            npm.Ready();
            var package = npm.Session.Packages.Single();

            npm.Presenter.Answers.Enqueue(2);                                     // Deny
            var denied = npm.Wait(npm.Session.RunAsync(package, package.Script("build")!));
            Check.True(denied.End == NpmRunEnd.NotRun && inner.Ran.Count == 0, $"denied: nothing ran ({denied.End}, {denied.Reason})");
            var question = npm.Presenter.Questions.Single();
            Check.Equal("The npm plug-in wants to run a command", question.Title, "the question names who asks (CommandLimits.Requester)");
            Check.True(question.Details.Contains("Runs: the script build of package.json: ng build --configuration production"), $"and the script's text (CommandLimits.Purpose): {string.Join(" | ", question.Details)}");
            Check.True(question.Message.Contains("npm-cli.js", StringComparison.Ordinal) && question.Message.Contains("run build", StringComparison.Ordinal) && question.Choices[2] == "Deny" && question.Kind is null,
                $"the command line node npm-cli.js run build, a plain Deny, a dialog not the agent's timeline ({question.Message})");
            Check.True(question.Details.Contains("Time limit: 1800 s"), "the time limit of a script that ends (30 minutes)");

            npm.Presenter.Answers.Enqueue(0);                                     // Allow once
            inner.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var running = npm.Session.RunAsync(package, package.Script("build")!);
            npm.Until(() => npm.Host.ActivityCenter.Running.Any(a => a.Title == "npm: build" && a.Text?.Contains("bundle generation complete", StringComparison.Ordinal) == true),
                "the run's activity with its last line");
            Check.True(!npm.Host.ActivityCenter.Running.Any(a => a.Title.StartsWith("Sandbox:", StringComparison.Ordinal)), "approve-each adds no second activity for a plug-in's command");
            inner.Hold.TrySetResult();
            inner.Hold = null;
            var ran = npm.Wait(running);
            Check.True(ran is { End: NpmRunEnd.Succeeded, ExitCode: 0 }, $"allowed: it ran ({ran.End}, {ran.Reason})");
            var (command, folder, limits) = inner.Ran.Single();
            Check.True(command.SequenceEqual([npm.Session.Install.Node!, npm.Session.Install.NpmCli!, "run", "build"]) && folder == npm.Root,
                "through approve-each into its inner sandbox: node npm-cli.js run build in the package's folder");
            Check.True(limits is { Requester: NpmRuns.Requester, Root: var root } && root == npm.Root && limits.Time == TimeSpan.FromMinutes(30)
                && limits.Environment!["npm_config_fund"] == "false" && limits.Environment["NG_CLI_ANALYTICS"] == "false" && limits.Environment["FORCE_COLOR"] == "1",
                "its limits: the npm plug-in asks, the workspace, 30 minutes, the quiet variables, colours");
            var tab = npm.Terminals.Outputs.Single(o => o.Name == "npm: build");
            npm.Until(() => tab.Text.ToString().Contains("exit code 0", StringComparison.Ordinal), "the end in the tab");
            string shown = tab.Text.ToString();
            Check.True(shown.Contains("$ npm run build", StringComparison.Ordinal) && shown.Contains("\e[32m> site@1.0.0 build", StringComparison.Ordinal) && shown.Contains("bundle generation complete", StringComparison.Ordinal),
                "its output streamed to the tab \"npm: build\", colours kept");

            // A long-running script: no time limit; Cancel ends it.
            inner.WaitForCancel = true;
            npm.Presenter.Answers.Enqueue(0);
            var dev = npm.Session.RunAsync(package, package.Script("dev")!);
            npm.Until(() => inner.Started.Task.IsCompleted, "the dev server started");
            Check.True(npm.Presenter.Questions.Last().Details.Any(d => d.StartsWith("Time limit: none", StringComparison.Ordinal)) && inner.Ran.Last().Limits.Time == Timeout.InfiniteTimeSpan,
                "dev runs until stopped: no time limit, said so");
            var devActivity = npm.Host.ActivityCenter.Running.Single(a => a.Title == "npm: dev");
            Check.True(npm.Host.Activities.Cancel(devActivity.Id), "the activity's Cancel");
            var stopped = npm.Wait(dev);
            Check.True(stopped.End == NpmRunEnd.Stopped && inner.Cancelled, $"Cancel stops the run through the sandbox ({stopped.End})");
            npm.Until(() => npm.Terminals.Outputs.Single(o => o.Name == "npm: dev").Text.ToString().Contains("stopped", StringComparison.Ordinal), "said in its tab");

            string everything = string.Join("\n", npm.Host.RecentLog.Select(l => l.Message)) + string.Join("\n", npm.Terminals.Outputs.Select(o => o.Text.ToString()))
                                + string.Join("\n", npm.Presenter.Questions.SelectMany(q => q.Details.Append(q.Message)));
            Check.True(!everything.Contains("SECRET_TOKEN", StringComparison.Ordinal), ".npmrc is never read into the log, a tab or a question");
        }
        finally
        {
            CommandSandboxes.Unregister(inner.Name);
        }
    }

    [Test]
    public static void InstallIsAnActivityAndYarnPnpmAndBunAreSaid()
    {
        var sandbox = new FakeSandbox("webnpm-direct") { Lines = ["added 812 packages in 41s"] };
        CommandSandboxes.Register(sandbox);
        try
        {
            using var npm = new Fixture(root =>
            {
                Write(Path.Combine(root, "package.json"), "{\"scripts\":{\"build\":\"x\"}}");
                Write(Path.Combine(root, "web", "package.json"), "{\"scripts\":{\"dev\":\"vite\"}}");
                Write(Path.Combine(root, "web", "pnpm-lock.yaml"), "lockfileVersion: '9.0'\n");
                Write(Path.Combine(root, "api", "package.json"), "{\"packageManager\":\"yarn@4.5.0\"}");
            }, vars: new() { ["CRAFT_PLUGIN_NPM_SANDBOX"] = sandbox.Name, ["CRAFT_PLUGIN_NPM_INSTALL"] = "ci" });
            npm.Ready();
            var root = npm.Session.Packages.Single(p => p.File == "package.json");
            sandbox.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var installing = npm.Session.InstallAsync(root);
            npm.Until(() => npm.Host.ActivityCenter.Running.Any(a => a.Title == "npm: install" && a.Text is { } text && text.StartsWith("0:0", StringComparison.Ordinal)
                && text.Contains(" · added 812 packages", StringComparison.Ordinal)), "the activity shows the elapsed time and the last line");
            sandbox.Hold.TrySetResult();
            sandbox.Hold = null;
            var installed = npm.Wait(installing);
            Check.True(installed.End == NpmRunEnd.Succeeded && sandbox.Ran.Single().Command.Skip(2).SequenceEqual(["install"]), "ci without package-lock.json runs install");
            var tab = npm.Terminals.Outputs.Single(o => o.Name == "npm: install");
            npm.Until(() => tab.Text.ToString().Contains("exit code 0", StringComparison.Ordinal), "its end in its tab");
            Check.True(tab.Text.ToString().Contains("npm ci needs package-lock.json", StringComparison.Ordinal), "and said why in the tab");

            Write(Path.Combine(npm.Root, "package-lock.json"), "{}");
            npm.Wait(npm.Session.ScanAsync());
            npm.Wait(npm.Session.InstallAsync(npm.Session.Packages.Single(p => p.File == "package.json")));
            Check.True(sandbox.Ran.Last().Command.Skip(2).SequenceEqual(["ci"]), "with its lock file, ci");

            var notes = npm.Host.Notifications.Entries.Where(e => e.Title.EndsWith("is not supported yet", StringComparison.Ordinal)).Select(e => e.Title).ToList();
            Check.True(notes.Contains("npm: pnpm is not supported yet") && notes.Contains("npm: yarn is not supported yet"), $"pnpm (its lock) and yarn (packageManager) said ({string.Join(", ", notes)})");
            npm.Wait(npm.Session.ScanAsync());
            Check.Equal(2, npm.Host.Notifications.Entries.Count(e => e.Title.EndsWith("is not supported yet", StringComparison.Ordinal)), "once per workspace, not on each read");
            int before = sandbox.Ran.Count;
            var pnpm = npm.Wait(npm.Session.InstallAsync(npm.Session.Packages.Single(p => p.File == "web/package.json")));
            Check.True(pnpm.End == NpmRunEnd.NotRun && pnpm.Reason!.Contains("pnpm", StringComparison.Ordinal) && sandbox.Ran.Count == before, "a pnpm package is not installed with npm");
            Check.True(EditorCommands.Find(NpmPlugin.ScriptCommandId("web/package.json", "dev")) is not null, "its scripts still run with npm");

            var missing = npm.Session;
            npm.Vars["CRAFT_PLUGIN_NPM_SANDBOX"] = "webnpm-nowhere";
            var refused = npm.Wait(missing.RunAsync(root, root.Script("build")!));
            Check.True(refused.End == NpmRunEnd.NotRun && refused.Reason!.Contains("not registered", StringComparison.Ordinal) && refused.Reason.Contains("CRAFT_PLUGIN_NPM_SANDBOX", StringComparison.Ordinal),
                $"a sandbox that is not registered: said, nothing runs ({refused.Reason})");
        }
        finally
        {
            CommandSandboxes.Unregister(sandbox.Name);
        }
    }

    // ---- the plug-in from its folder ----

    [Test]
    public static void TheNpmPluginLoadsFromItsFolderAndCanBeRemoved()
    {
        Check.True(EditorPlugins.Find(NpmSettings.PluginName) is null, "before it loads, \"npm\" is not registered");
        var icons = (string[])["package.json", "package-lock.json", ".npmrc", ".nvmrc"];
        Check.True(icons.All(name => FileIcons.For(name) is { Shape: not null } icon && icon != FileIcons.Plain) && FileIcons.ForFolder("node_modules") != FileIcons.PlainFolder,
            "npm's files and node_modules have the library's drawn icons (W-files), so the plug-in registers none to shadow them");
        using var temp = new TempFolder();
        var output = PhasePTests.PluginOutput(NpmPluginPackage.PackageName);
        var folder = Path.Combine(temp.Path, "plugins", NpmPluginPackage.PackageName);
        foreach (var file in Directory.GetFiles(output, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(folder, Path.GetRelativePath(output, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        Check.True(!File.Exists(Path.Combine(folder, "Craft.Abstractions.dll")) && !File.Exists(Path.Combine(folder, "Idrak.Abstraction.dll")), "it carries no copy of the shared contracts");
        var result = CraftPlugins.LoadFolder(folder);
        try
        {
            Check.True(result is { Loaded: true, Manifest.ContractVersion: "1.1" }, $"it loads from its folder ({result.Error})");
            var plugin = EditorPlugins.Get(NpmSettings.PluginName);
            Check.True(AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly) != AssemblyLoadContext.Default, "\"npm\" comes from the plug-in's own load context");
            Check.True(plugin.Settings.All(s => s.Shape?.Section == SettingSections.Terminal) && plugin.Settings.Count(s => s.MachineOnly) == 3, "its settings under Terminal › npm, three machine-only");
        }
        finally
        {
            var loaded = AssemblyLoadContext.All.Where(c => c != AssemblyLoadContext.Default).SelectMany(c => c.Assemblies)
                .LastOrDefault(x => x.GetName().Name == NpmPluginPackage.PackageName && x.Location.StartsWith(folder, StringComparison.Ordinal));
            if (loaded is not null && IdrakPlugins.PackageOf(loaded) is { } package)
            {
                IdrakPlugins.UnregisterAll(package);
            }
        }

        Check.True(EditorPlugins.Find(NpmSettings.PluginName) is null && icons.All(name => FileIcons.For(name) != FileIcons.Plain), "removed: the plug-in is gone, the icons stay the library's");
    }

    // ---- real Node (skipped without it) ----

    [Test]
    public static void RealNodeRunsScriptsThroughTheProcessSandboxAndAScriptReadsInputInATerminal()
    {
        var found = NodeInstall.Find(new NpmSettings(), Environment.GetEnvironmentVariable, OperatingSystem.IsWindows());
        if (!found.Ready)
        {
            Console.WriteLine($"  skipped: {found.Problem}");
            return;
        }

        if (CommandSandboxes.Find(CommandSandboxes.Process) is null)
        {
            CommandSandboxes.Register(new Idrak.Text.Generation.ProcessCommandSandbox());
        }

        using var npm = new Fixture(root => Write(Path.Combine(root, "package.json"), """
            {
              "name": "real-node",
              "scripts": {
                "hello": "node -e \"console.log('hi ' + process.env.FORCE_COLOR + ' ' + process.env.npm_config_fund)\"",
                "wait": "node -e \"setInterval(() => {}, 1000)\"",
                "ask": "node -e \"process.stdout.write('name? '); process.stdin.once('data', d => { console.log('got ' + String(d).trim()); process.exit(0); })\""
              }
            }
            """), node: "", vars: new() { ["CRAFT_PLUGIN_NPM_SANDBOX"] = CommandSandboxes.Process });
        npm.Ready();
        var session = npm.Session;
        Check.True(session.Install.Version is { } version && session.Status!.Text == "Node " + version, $"Node's version read once into the status item ({session.Status?.Text})");
        var package = session.Packages.Single();

        var hello = npm.Wait(session.RunAsync(package, package.Script("hello")!), 60);
        Check.True(hello is { End: NpmRunEnd.Succeeded, ExitCode: 0 }, $"npm run hello ran ({hello.End}, {hello.Reason})");
        var tab = npm.Terminals.Outputs.Single(o => o.Name == "npm: hello");
        npm.Until(() => tab.Text.ToString().Contains("hi 1 false", StringComparison.Ordinal), "its output, with the run's variables");

        var wait = session.RunAsync(package, package.Script("wait")!);
        npm.Until(() => npm.Host.ActivityCenter.Running.Any(a => a.Title == "npm: wait"), "the endless script runs");
        Thread.Sleep(500);
        var clock = Stopwatch.StartNew();
        npm.Host.Activities.Cancel(npm.Host.ActivityCenter.Running.Single(a => a.Title == "npm: wait").Id);
        var stopped = npm.Wait(wait, 30);
        Check.True(stopped.End == NpmRunEnd.Stopped && clock.Elapsed < TimeSpan.FromSeconds(20), $"Cancel stops it with its process tree ({stopped.End}, {clock.Elapsed.TotalSeconds:0.0} s)");

        var ask = session.RunAsync(package, package.Script("ask")!, terminal: true);
        npm.Until(() => npm.Terminals.Programs.Count == 1 && npm.Terminals.Programs[0].Text.Contains("name? ", StringComparison.Ordinal), "the script asks in its terminal tab", 60);
        var program = npm.Terminals.Programs[0];
        Check.Equal("npm: ask", program.Request.Name, "the tab's name");
        program.Request.Input.Write("Ada\n"u8);
        program.Request.Input.Flush();
        var answered = npm.Wait(ask, 60);
        npm.Until(() => program.Text.Contains("got Ada", StringComparison.Ordinal) && program.Ended is not null, "the answer and the end in the tab");
        Check.True(answered is { End: NpmRunEnd.Succeeded, ExitCode: 0 } && program.Ended!.Contains("exit code 0", StringComparison.Ordinal), $"it read what was typed and ended ({answered.End})");
    }

    // ---- helpers ----

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>A workspace with the npm plug-in (and approve-each when asked) active in a host whose UI thread the test pumps; Node a fake one unless <c>node</c> is "" (PATH's).</summary>
    private sealed class Fixture : IDisposable
    {
        private readonly TempFolder _temp = new();
        private readonly ConcurrentQueue<Action> _queue = new();
        private readonly NpmPluginPackage _package = new();
        private readonly SandboxPackage? _sandbox;
        private readonly SynchronizationContext? _before = SynchronizationContext.Current;

        public Fixture(Action<string> workspace, string? node = null, bool approveEach = false, Dictionary<string, string>? vars = null)
        {
            Root = Path.GetFullPath(Path.Combine(_temp.Path, "ws"));
            Directory.CreateDirectory(Root);
            workspace(Root);
            Vars = vars ?? [];
            if (node is null)
            {
                string fake = Path.Combine(_temp.Path, "node", "bin", NodeName);
                Write(fake, "not a program");
                Write(Path.Combine(_temp.Path, "node", "lib", "node_modules", "npm", "bin", "npm-cli.js"), "// npm");
                Vars["CRAFT_PLUGIN_NPM_NODE"] = fake;
            }
            else if (node.Length > 0)
            {
                Vars["CRAFT_PLUGIN_NPM_NODE"] = Path.Combine(_temp.Path, node, NodeName);
            }

            SynchronizationContext.SetSynchronizationContext(new QueueContext(_queue));
            IdrakPlugins.RegisterAll(_package);
            if (approveEach)
            {
                _sandbox = new SandboxPackage();
                IdrakPlugins.RegisterAll(_sandbox);
            }

            Host = new EditorHost(new CraftOptions { DataFolder = Path.Combine(_temp.Path, "data") }, _queue.Enqueue, Presenter, name => Vars.TryGetValue(name, out var v) ? v : null)
            {
                WorkspaceFolder = Root,
                Terminals = Terminals,
            };
            Host.RefreshPlugins();
            Pump();
        }

        public string Root { get; }

        public Dictionary<string, string> Vars { get; }

        public EditorHost Host { get; }

        public FakeTerminals Terminals { get; } = new();

        public AskingPresenter Presenter { get; } = new();

#pragma warning disable CA1822 // The fixture's own view of the plug-in's active session.
        public NpmSession Session => NpmPlugin.Session ?? throw new InvalidOperationException("the npm plug-in did not activate");
#pragma warning restore CA1822

        /// <summary>Waits until Node was looked for and, when found, the first scan is in.</summary>
        public void Ready()
        {
            Until(() => Session.Ready.IsCompleted, "Node looked for", 30);
            if (Session.Install.Ready)
            {
                Until(() => Session.Scanned.IsCompleted && Session.Packages.Count > 0, "the workspace's scripts read");
            }
        }

        public void Pump()
        {
            while (_queue.TryDequeue(out var action))
            {
                action();
            }
        }

        public void Until(Func<bool> done, string what, int seconds = 20)
        {
            var watch = Stopwatch.StartNew();
            while (true)
            {
                Pump();
                if (done())
                {
                    return;
                }

                Check.True(watch.Elapsed < TimeSpan.FromSeconds(seconds), $"{what} (waited {seconds} s)");
                Thread.Sleep(5);
            }
        }

        public T Wait<T>(Task<T> task, int seconds = 20)
        {
            Until(() => task.IsCompleted, "the task ends", seconds);
            return task.GetAwaiter().GetResult();
        }

        public void Wait(Task task)
        {
            Until(() => task.IsCompleted, "the task ends");
            task.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Pump();
            Host.Dispose();
            IdrakPlugins.UnregisterAll(_package);
            if (_sandbox is not null)
            {
                IdrakPlugins.UnregisterAll(_sandbox);
            }

            SynchronizationContext.SetSynchronizationContext(_before);
            _temp.Dispose();
        }
    }

    private sealed class QueueContext(ConcurrentQueue<Action> queue) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => queue.Enqueue(() => d(state));

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        public override SynchronizationContext CreateCopy() => this;
    }

    private sealed class FakeSandbox(string name) : ICommandSandbox
    {
        public List<(IReadOnlyList<string> Command, string Folder, CommandLimits Limits)> Ran { get; } = [];

        public IReadOnlyList<string> Lines { get; init; } = [];

        public bool WaitForCancel { get; set; }

        /// <summary>When set, a run waits for it after its output (so its activity can be seen running).</summary>
        public TaskCompletionSource? Hold { get; set; }

        public bool Cancelled { get; private set; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Name { get; } = name;

        public string Summary => "records each command and plays its output";

        public string? Check(CommandLimits limits) => null;

        public async ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
        {
            lock (Ran)
            {
                Ran.Add((command, directory, limits));
            }

            foreach (var line in Lines)
            {
                limits.Output?.Invoke(line);
            }

            if (Hold is { } hold)
            {
                await hold.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (WaitForCancel)
            {
                Started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Cancelled = true;
                    throw;
                }
            }

            return new CommandResult(0, false, string.Join('\n', Lines), TimeSpan.FromMilliseconds(5), false);
        }
    }

    private sealed class AskingPresenter : IEditorPresenter
    {
        public ConcurrentQueue<int> Answers { get; } = new();

        public List<EditorQuestion> Questions { get; } = [];

        public Task<bool> ConfirmEditsAsync(EditProposal proposal, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Task<int> AskAsync(EditorQuestion question, CancellationToken cancellationToken)
        {
            lock (Questions)
            {
                Questions.Add(question);
            }

            return Task.FromResult(Answers.TryDequeue(out int answer) ? answer : question.Dismissed);
        }
    }

    private sealed class FakeTerminals : IEditorTerminals
    {
        public List<FakeOutput> Outputs { get; } = [];

        public List<FakeProgram> Programs { get; } = [];

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

        public IEditorTerminalProgram? OpenProgram(EditorProgramRequest request)
        {
            var program = new FakeProgram(request);
            Programs.Add(program);
            return program;
        }
    }

    private sealed class FakeOutput(string name) : IEditorTerminalOutput
    {
        public string Name { get; } = name;

        public StringBuilder Text { get; } = new();

        public string Title => Name;

        public bool IsOpen => true;

        public bool Append(string text)
        {
            lock (Text)
            {
                Text.Append(text);
            }

            return true;
        }

        public void Show()
        {
        }
    }

    /// <summary>A program tab: the program's output read as it comes; the test writes its input.</summary>
    private sealed class FakeProgram : IEditorTerminalProgram
    {
        private readonly StringBuilder _text = new();

        public FakeProgram(EditorProgramRequest request)
        {
            Request = request;
            OutputEnded = Task.Run(async () =>
            {
                var buffer = new byte[4096];
                int read;
                while ((read = await request.Output.ReadAsync(buffer)) > 0)
                {
                    lock (_text)
                    {
                        _text.Append(Encoding.UTF8.GetString(buffer, 0, read));
                    }
                }
            });
        }

        public EditorProgramRequest Request { get; }

        public string Text
        {
            get
            {
                lock (_text)
                {
                    return _text.ToString();
                }
            }
        }

        public string? Ended { get; private set; }

        public Task OutputEnded { get; }

        public string Title => Request.Name;

        public bool IsOpen => Ended is null;

        public bool Append(string text)
        {
            lock (_text)
            {
                _text.Append(text);
            }

            return true;
        }

        public void Show()
        {
        }

        public void End(string? text = null, int exitCode = 0) => Ended = text ?? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"exit code {exitCode}");
    }
}
