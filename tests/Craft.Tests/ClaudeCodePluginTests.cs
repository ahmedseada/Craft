// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Text.Json.Nodes;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Diagnostics;
using Craft.Core.Documents;
using Craft.Editor;
using Craft.Editor.Shell;
using Craft.Plugins.ClaudeCode;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// The Claude Code plug-in (plugins/Craft.Plugins.ClaudeCode), headless: finding the CLI on a fake PATH, its flags and the
/// headless arguments, the accounts read as text from PowerShell profiles and Windows Terminal, the MCP config written with
/// the address and the token outside the workspace, the stream read as steps, a whole run through a stand-in process, each
/// diagnostic MCP tool's bounded output from fake reports, run_tests refusing a missing filter and going through the
/// sandbox, the commands' prompts naming the right report, and the agent chip listing the engine's models.
/// </summary>
[TestGroup("ClaudeCodePlugin")]
public static class ClaudeCodePluginTests
{
    [Test]
    public static void TheProgramIsFoundOnAFakePathAndItsFlagsRead()
    {
        var windows = ClaudeProgram.Find("", out var why, searchPath: @"C:\tools;C:\Users\me\AppData\Roaming\npm", extensions: ".EXE;.CMD",
            exists: p => p == Path.Combine(@"C:\Users\me\AppData\Roaming\npm", "claude.cmd"));
        Check.True(windows is { IsScript: true } && windows.Path == Path.Combine(@"C:\Users\me\AppData\Roaming\npm", "claude.cmd") && why is null,
            "on Windows the npm claude.cmd is found with PATHEXT, and started through cmd");
        var unix = ClaudeProgram.Find("", out _, searchPath: "/usr/local/bin:/usr/bin", extensions: "", exists: p => p == Path.Combine("/usr/bin", "claude"));
        Check.True(unix is { IsScript: false } && unix.Path == Path.Combine("/usr/bin", "claude"), "elsewhere claude itself");
        Check.True(ClaudeProgram.Find("", out var none, searchPath: "/nowhere", extensions: "", exists: _ => false) is null && none!.Contains("npm install -g @anthropic-ai/claude-code", StringComparison.Ordinal),
            "not found: said with the install hint (nothing installed)");
        Check.True(ClaudeProgram.Find("/opt/missing/claude", out var configured, exists: _ => false) is null && configured!.Contains("CRAFT_PLUGIN_CLAUDECODE_PATH", StringComparison.Ordinal),
            "a configured path that is missing names its setting");
        Check.True(ClaudeProgram.Unavailable is null, "a desktop runs Claude Code");
        Check.Equal("2.0.31", ClaudeProgram.VersionOf("2.0.31 (Claude Code)\n"), "the version");
        Check.Equal("\"C:\\a b\\x.json\"", ClaudeProgram.Quote(@"C:\a b\x.json"), "a path with a space is quoted");
        Check.Equal("\"say \\\"hi\\\"\"", ClaudeProgram.Quote("say \"hi\""), "quotes are escaped");
        Check.Equal("\"C:\\a b\\\\\"", ClaudeProgram.Quote(@"C:\a b\"), "a trailing backslash is doubled before the closing quote");
        Check.Equal("plain", ClaudeProgram.Quote("plain"), "a plain word stays");

        var flags = new ClaudeFlags("2.0.31", "Usage: claude [options]\n  -p, --print  Print the response\n  --output-format <format>\n  --verbose\n  --mcp-config <configs...>\n  --permission-mode <mode>\n  -r, --resume [sessionId]\n");
        Check.True(flags.MissingForHeadless().Count == 0 && new ClaudeFlags("1.0", "Usage: claude").MissingForHeadless().Count == 2, "the headless flags read from --help");
        var notes = new List<string>();
        var arguments = ClaudeCodeSession.Arguments(flags, new ClaudeCodeSettings { Arguments = "--add-dir \"D:\\other dir\"" }, "/data/mcp.json", "session-7", notes);
        Check.SequenceEqual(["--print", "--output-format", "stream-json", "--verbose", "--permission-mode", "acceptEdits", "--mcp-config", "/data/mcp.json", "--resume", "session-7", "--add-dir", "D:\\other dir"],
            arguments, "print mode, the stream, the mode, Craft's MCP config, the session resumed, the user's own arguments");
        Check.True(notes.Single().Contains("--permission-prompt-tool is not supported by this version", StringComparison.Ordinal), "a flag this version lacks is said, and left out");
        notes.Clear();
        var full = new ClaudeFlags("2.1", flags.Help + "  --permission-prompt-tool <tool>\n");
        Check.True(ClaudeCodeSession.Arguments(full, new ClaudeCodeSettings(), "/m.json", null, notes) is var withApprovals
            && withApprovals.Contains("mcp__craft__claude_code_approve") && notes.Count == 0, "with the flag, Claude Code asks Craft before what needs permission");
        Check.True(!ClaudeCodeSession.Arguments(full, new ClaudeCodeSettings(), null, null, notes).Contains("--mcp-config"), "with the server off, no MCP config");
    }

    [Test]
    public static void AccountsAreReadAsTextFromProfilesAndWindowsTerminal()
    {
        const string profile = """
            function claude-personal {
                $env:CLAUDE_CONFIG_DIR = "$HOME\.claude-personal"
                claude @args
            }
            function claude-work { $env:CLAUDE_CONFIG_DIR = Join-Path $HOME '.claude-work'; claude @args }
            function claude-other { claude --verbose @args }
            Set-Alias claude-w2 Start-Work
            function Start-Work { $env:CLAUDE_CONFIG_DIR = 'D:\cfg\work2' }
            """;
        Func<string, string?> variable = name => name == "USERPROFILE" ? @"C:\Users\me" : null;
        var found = ClaudeAccounts.FromPowerShellProfile(profile, "profile.ps1", variable);
        Check.SequenceEqual(["claude-personal", "claude-work", "claude-other", "claude-w2"], [.. found.Select(a => a.Name)], "the claude-* functions and the alias, never run");
        Check.Equal(@"C:\Users\me\.claude-personal", found[0].Environment[ClaudeAccounts.ConfigVariable], "$HOME expanded in a double-quoted value");
        Check.Equal(Path.Combine(@"C:\Users\me", ".claude-work"), found[1].Environment[ClaudeAccounts.ConfigVariable], "Join-Path $HOME '.claude-work'");
        Check.True(found[2].Problem is { } problem && problem.Contains("CRAFT_PLUGIN_CLAUDECODE_ACCOUNTS", StringComparison.Ordinal), "a function that sets nothing readable says what to do");
        Check.Equal(@"D:\cfg\work2", found[3].Environment[ClaudeAccounts.ConfigVariable], "an alias takes its function's variables");

        const string terminal = """
            // Windows Terminal's settings (comments and trailing commas allowed)
            { "profiles": { "list": [
                { "name": "claude-work", "commandline": "pwsh.exe -NoExit -Command claude-work" },
                { "name": "claude-cmd", "commandline": "cmd.exe /k set CLAUDE_CONFIG_DIR=C:\\cfg\\cmd&& claude" },
                { "name": "Claude Env", "environment": { "CLAUDE_CONFIG_DIR": "%USERPROFILE%\\x" }, "commandline": "claude" },
                { "name": "PowerShell" },
            ], }, }
            """;
        var profiles = ClaudeAccounts.FromWindowsTerminal(terminal, found, variable);
        Check.SequenceEqual(["claude-work", "claude-cmd", "Claude Env"], [.. profiles.Select(a => a.Name)], "Windows Terminal's profiles named claude*");
        Check.Equal(Path.Combine(@"C:\Users\me", ".claude-work"), profiles[0].Environment[ClaudeAccounts.ConfigVariable], "a profile calling a function takes the function's variables");
        Check.Equal(@"C:\cfg\cmd", profiles[1].Environment[ClaudeAccounts.ConfigVariable], "cmd's set NAME=value");

        var configured = ClaudeAccounts.Parse("claude-work=E:\\mine|team=CLAUDE_CONFIG_DIR=F:\\t;HTTPS_PROXY=http://p");
        Check.True(configured[0].Environment[ClaudeAccounts.ConfigVariable] == "E:\\mine" && configured[1].Environment["HTTPS_PROXY"] == "http://p", "the setting: a folder, or variables");
        var merged = ClaudeAccounts.Merge(configured, [.. found, .. profiles]);
        Check.True(merged.Count(a => a.Name == "claude-work") == 1 && merged.First(a => a.Name == "claude-work").Source == "settings", "a name once: the setting's wins");
        Check.Throws<ArgumentException>(() => ClaudeAccounts.Parse("=x"), "an entry with no name is refused, naming the setting");
        Check.True(ClaudeAccounts.ModelOf("claude-work") == "Claude Code (claude-work)" && ClaudeAccounts.AccountOf("Claude Code (claude-work)") == "claude-work"
            && ClaudeAccounts.AccountOf(ClaudeAccounts.PlainModel) is null, "an account's model at the chip, and back");
        var detected = ClaudeAccounts.Detect("Docs", "Local", variable, path =>
            path == ClaudeAccounts.ProfilePaths("Docs")[0] ? profile : path == ClaudeAccounts.TerminalSettingsPaths("Local")[0] ? terminal : null);
        Check.True(detected.Any(a => a.Name == "claude-cmd") && detected.Count(a => a.Name == "claude-work") == 1, "detection reads the profile and the terminal's settings, one account a name");
    }

    [Test]
    public static void TheMcpConfigHoldsTheAddressAndTokenOutsideTheWorkspace()
    {
        using var folder = new TempFolder();
        string data = Path.Combine(folder.Path, "data");
        string workspace = Path.Combine(folder.Path, "project");
        Directory.CreateDirectory(workspace);
        string path = McpConfig.Write(data, workspace, "http://127.0.0.1:5123/mcp", "tok-123");
        Check.True(path == Path.Combine(data, "claude-code", "mcp-craft.json") && !McpConfig.IsInside(path, workspace), "written in the user's data folder, never the workspace");
        var json = JsonNode.Parse(File.ReadAllText(path))!;
        Check.True(json["mcpServers"]?["craft"]?["type"]?.ToString() == "http" && json["mcpServers"]!["craft"]!["url"]!.ToString() == "http://127.0.0.1:5123/mcp"
            && json["mcpServers"]!["craft"]!["headers"]!["Authorization"]!.ToString() == "Bearer tok-123", "the server 'craft' over HTTP with its bearer token");
        if (!OperatingSystem.IsWindows())
        {
            Check.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path), "readable by the user only");
        }

        Check.True(!File.Exists(path + ".tmp"), "no temporary file is left");
        Check.True(JsonNode.Parse(McpConfig.Text("http://x/mcp", null))!["mcpServers"]!["craft"]!["headers"] is null, "no token: no header");
        Check.Throws<InvalidOperationException>(() => McpConfig.Write(Path.Combine(workspace, ".data"), workspace, "http://x/mcp", "t"), "a data folder inside the workspace is refused (the token would be in the project)");
    }

    [Test]
    public static void TheStreamIsReadAsTheAgentsSteps()
    {
        var stream = new ClaudeStream();
        var steps = Lines().SelectMany(stream.Read).ToList();
        Check.True(steps[0] is { Step: AgentEngineStep.Note } && steps[0].Text.Contains("craft: connected", StringComparison.Ordinal) && stream.CraftConnected == true,
            "its start: the model and Craft's MCP server connected");
        Check.True(steps[1] is { Step: AgentEngineStep.Text, Text: "Looking." }, "its text");
        Check.True(steps[2] is { Step: AgentEngineStep.ToolCall, Text: "Bash", Id: "t1", Detail: "dotnet build" }, "a tool call with its command");
        Check.True(steps[3] is { Step: AgentEngineStep.ToolResult, Text: "Bash", Id: "t1", Ok: true, Detail: "Build succeeded." }, "its result, the first line");
        Check.True(stream is { Ended: true, Failed: false, Session: "s-9" } && stream.End == "Done in 3 turns · 4.1 s · $0.12.", $"its end: {stream.End}");
        Check.True(stream.Read("not json").Count == 0 && stream.Read("{\"type\":\"other\"}").Count == 0, "a line it does not know is passed over");
        var failed = new ClaudeStream();
        var error = failed.Read("{\"type\":\"result\",\"subtype\":\"error_max_turns\",\"is_error\":true,\"num_turns\":30}");
        Check.True(failed.Failed && error.Single() is { Step: AgentEngineStep.Error } && failed.End.Contains("turn limit", StringComparison.Ordinal), "a failed result says why");
        Check.True(ClaudeStream.Cut(new string('x', 50), 10).StartsWith("xxxxxxxxxx… (40 more characters)", StringComparison.Ordinal), "a long text is cut, said so");
    }

    [Test]
    public static async Task AWholeRunThroughAStandInProcess()
    {
        using var folder = new TempFolder();
        string file = Path.Combine(folder.Path, "stream.jsonl");
        await File.WriteAllLinesAsync(file, Lines());
        ProcessStartInfo info;
        if (OperatingSystem.IsWindows())
        {
            info = new ProcessStartInfo("cmd.exe", $"/d /c type \"{file}\"");
        }
        else
        {
            info = new ProcessStartInfo("/bin/sh");
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("cat > /dev/null; cat \"$0\"");
            info.ArgumentList.Add(file);
        }

        info.UseShellExecute = false;
        info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
        info.WorkingDirectory = folder.Path;
        var steps = new List<AgentEngineEvent>();
        var result = await ClaudeCodeSession.RunProcessAsync(info, "Fix the build", steps.Add, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        Check.True(result is { Failed: false, Session: "s-9" } && steps.Count == 4, $"the stream's steps and the session for a follow-up ({result.End}, {steps.Count} steps)");
        var broken = new ProcessStartInfo(OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (OperatingSystem.IsWindows())
        {
            broken.Arguments = "/d /c echo not signed in 1>&2 & exit 3";
        }
        else
        {
            broken.ArgumentList.Add("-c");
            broken.ArgumentList.Add("echo 'not signed in' >&2; exit 3");
        }

        var failed = await ClaudeCodeSession.RunProcessAsync(broken, "x", _ => { }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        Check.True(failed.Failed && failed.End.Contains("exit code 3", StringComparison.Ordinal) && failed.End.Contains("not signed in", StringComparison.Ordinal),
            $"an end before the result says the exit code, its error output and what to do: {failed.End}");
    }

    [Test]
    public static async Task EachDiagnosticToolIsBoundedOnFakeReports()
    {
        using var folder = new TempFolder();
        string crashes = Path.Combine(folder.Path, "crashes");
        Directory.CreateDirectory(crashes);
        Write(crashes, "crash-20261101-090000-000.txt", "Craft crash report\n" + new string('c', 5000), 1);
        Write(crashes, "callback-20261101-100000-000.txt", "A platform callback failed\nInvalidOperationException: the window has no content", 2);
        Write(crashes, "stall-20261101-110000-000.txt", "The window was blocked 3.2 s by editor: paint", 3);
        Write(crashes, "notes.txt", "not a report", 4);
        bool registered = EditorPlugins.Find(ClaudeCodePlugin.Instance.Name) is null;
        if (registered)
        {
            EditorPlugins.Register(ClaudeCodePlugin.Instance);              // its declared settings are read through the host
        }

        using var trail = new CrashTrail(Path.Combine(crashes, "trail.log"));
        try
        {
            var variables = new Dictionary<string, string>
            {
                ["CRAFT_PLUGIN_CLAUDECODE_TOOLOUTPUTCHARACTERS"] = "600",
                ["CRAFT_PLUGIN_CLAUDECODE_TESTSANDBOX"] = "test-recording",
                ["CRAFT_PLUGIN_CLAUDECODE_CRAFTREPOSITORY"] = folder.Path,
            };
            using var host = new EditorHost(new CraftOptions { CrashFolder = crashes, HostLogLines = 50 }, readVariable: name => variables.GetValueOrDefault(name)) { WorkspaceFolder = folder.Path };
            host.Trail = trail;
            for (int i = 0; i < 30; i++)
            {
                host.Log(EditorLogLevel.Information, $"line {i}");
            }

            await trail.FlushAsync();
            host.Diagnostics.MeasureSource = () => [new TelemetryMeasure("host", "editor.paint.warm", 2.1, 2.4, 6.2, 900, "ms"), new TelemetryMeasure("host", "ui.stall", 3200, 3200, 3200, 1, "ms")];
            var document = host.Open(TextDocument.Create("first\nsecond", new CraftOptions()), null);
            host.SetDiagnostics(document, [new EditorDiagnostic(6, 6, DiagnosticSeverity.Error, "CS1002", "; expected")]);
            var tools = DiagnosticToolSet.Instance.Live(host).ToDictionary(t => t.Definition.Name);

            string reports = await Call(tools, DiagnosticToolNames.Reports, new JsonObject());
            Check.True(reports.Contains("3 reports", StringComparison.Ordinal) && reports.IndexOf("stall-", StringComparison.Ordinal) < reports.IndexOf("crash-", StringComparison.Ordinal)
                && !reports.Contains("notes.txt", StringComparison.Ordinal), $"the three reports, the newest first: {reports}");
            Check.True((await Call(tools, DiagnosticToolNames.Reports, new JsonObject { ["kind"] = "stall" })).Contains("1 reports", StringComparison.Ordinal), "by kind");
            string crash = await Call(tools, DiagnosticToolNames.ReadReport, new JsonObject { ["name"] = "crash-20261101-090000-000.txt" });
            Check.True(crash.Contains("Craft crash report", StringComparison.Ordinal) && crash.Contains("more characters", StringComparison.Ordinal) && crash.Length < 900,
                $"a report's text, bounded by toolOutputCharacters ({crash.Length} characters)");
            Check.True((await Call(tools, DiagnosticToolNames.ReadReport, new JsonObject { ["name"] = "latest" })).Contains("blocked 3.2 s", StringComparison.Ordinal), "latest: the newest");
            Check.True((await Call(tools, DiagnosticToolNames.ReadReport, new JsonObject { ["name"] = "gone.txt" })).StartsWith("No report named", StringComparison.Ordinal), "a name not there is said");
            string output = await Call(tools, DiagnosticToolNames.Output, new JsonObject { ["lines"] = 5 });
            Check.True(output.Contains("line 29", StringComparison.Ordinal) && !output.Contains("line 24", StringComparison.Ordinal), "Output's last lines");
            Check.True((await Call(tools, DiagnosticToolNames.Trail, new JsonObject { ["lines"] = 10 })).Contains("line 29", StringComparison.Ordinal), "the crash trail's last lines (Output goes to it)");
            string telemetry = await Call(tools, DiagnosticToolNames.Telemetry, new JsonObject());
            Check.True(telemetry.IndexOf("ui.stall", StringComparison.Ordinal) < telemetry.IndexOf("editor.paint.warm", StringComparison.Ordinal), "the telemetry, the slowest first");
            Check.True((await Call(tools, DiagnosticToolNames.Problems, [])).Contains(":2:1: error CS1002: ; expected", StringComparison.Ordinal), "the problems with their line and column");
            string state = await Call(tools, DiagnosticToolNames.AppState, new JsonObject());
            Check.True(state.Contains("Open documents (1)", StringComparison.Ordinal) && state.Contains(folder.Path, StringComparison.Ordinal), "the app's state");
            Check.True((await Call(tools, DiagnosticToolNames.OpenFile, new JsonObject { ["path"] = "missing.cs" })).Contains("does not exist", StringComparison.Ordinal), "open_file on a missing file changes nothing");
            foreach (var tool in tools.Values)
            {
                Check.True(tool.Definition.Name != "write_file" && !tool.Definition.Name.Contains("edit", StringComparison.Ordinal), "no diagnostic tool edits: a change is propose_edit's diff");
            }

            string denied = await Call(tools, DiagnosticToolNames.Approve, new JsonObject { ["tool_name"] = "Bash", ["input"] = new JsonObject { ["command"] = "rm -rf /" } });
            Check.True(JsonNode.Parse(denied)!["behavior"]!.ToString() == "deny", "with nobody to answer, Claude Code's request is denied");
            Check.True(JsonNode.Parse(DiagnosticTools.Allow(new JsonObject { ["command"] = "ls" }))!["updatedInput"]!["command"]!.ToString() == "ls", "an allow keeps the input");

            // run_tests: a missing or too wide filter is refused; a good one goes through the sandbox, the user approving.
            var sandbox = new RecordingSandbox();
            CommandSandboxes.Register(sandbox);
            try
            {
                foreach (var bad in new[] { "", "  |  ", "a", "Ter*" })
                {
                    string refused = await Call(tools, DiagnosticToolNames.RunTests, new JsonObject { ["filter"] = bad });
                    Check.True(refused.StartsWith("Refused", StringComparison.Ordinal) && refused.Contains("full suite", StringComparison.Ordinal), $"'{bad}' is refused: {refused}");
                }

                Check.Equal(0, sandbox.Commands.Count, "nothing ran for a refused filter");
                string ran = await Call(tools, DiagnosticToolNames.RunTests, new JsonObject { ["filter"] = "Terminal|Layout", ["build"] = true });
                Check.True(sandbox.Commands.Count == 2 && sandbox.Commands[0].Command[1] == "build" && sandbox.Commands[1].Command.Contains("--no-build"), "it builds, then runs, each command through the sandbox");
                var limits = sandbox.Commands[1].Limits;
                Check.True(limits.Environment!["CRAFT_FILTER"] == "Terminal|Layout" && limits.Root == folder.Path && limits.Requester == "Claude Code" && sandbox.Commands[1].Directory == folder.Path,
                    "with CRAFT_FILTER, in Craft's repository, named as Claude Code's");
                Check.True(ran.Contains("exit code 0", StringComparison.Ordinal), "the run's outcome is answered");
            }
            finally
            {
                CommandSandboxes.Unregister(sandbox.Name);
            }
        }
        finally
        {
            if (registered)
            {
                EditorPlugins.Unregister(ClaudeCodePlugin.Instance.Name);
            }
        }
    }

    [Test]
    public static void TheCommandsPromptsNameTheRightReport()
    {
        var settings = new ClaudeCodeSettings();
        var stall = new DiagnosticReport(DiagnosticReportKinds.Stall, Path.Combine("crashes", "stall-1.txt"), DateTimeOffset.Now, 10);
        var callback = new DiagnosticReport(DiagnosticReportKinds.Callback, Path.Combine("crashes", "callback-1.txt"), DateTimeOffset.Now.AddMinutes(-1), 10);
        var crash = new DiagnosticReport(DiagnosticReportKinds.Crash, Path.Combine("crashes", "crash-1.txt"), DateTimeOffset.Now.AddMinutes(-2), 10);
        string onStall = ClaudeCodeSession.PromptFor(settings, stall, "D:/Projects/Craft", null);
        Check.True(onStall.Contains(stall.Path, StringComparison.Ordinal) && onStall.Contains("window was blocked", StringComparison.Ordinal) && onStall.Contains("craft_read_report", StringComparison.Ordinal)
            && onStall.Contains(Path.Combine("D:/Projects", "Idrak"), StringComparison.Ordinal), "a stall: the stall's prompt, the report and the tools named, Idrak beside Craft");
        string onFailure = ClaudeCodeSession.PromptFor(settings, callback, null, null);
        Check.True(onFailure.Contains(callback.Path, StringComparison.Ordinal) && onFailure.Contains("a failure it caught", StringComparison.Ordinal) && onFailure.Contains("CRAFT_FILTER", StringComparison.Ordinal),
            "a caught failure: the crash's prompt, never the full suite");
        var diagnostics = new FakeDiagnostics([stall, callback, crash]);
        Check.True(ClaudeCodeSession.Latest(diagnostics, DiagnosticReportKinds.Crash) == callback && ClaudeCodeSession.Latest(diagnostics, DiagnosticReportKinds.Stall) == stall,
            "diagnose the last crash takes the newest crash or caught failure; the last stall, the newest stall");
        Check.True(settings.PromptBuild.Contains("IDRAK_DEVICES", StringComparison.Ordinal) && settings.PromptBuild.Contains("IDRAK_FILTER", StringComparison.Ordinal)
            && settings.PromptBuild.Contains("net10.0-windows10.0.19041.0", StringComparison.Ordinal) && settings.PromptBuild.Contains("never the full suite", StringComparison.Ordinal),
            "build and test names Craft's and Idrak's targeted commands, the Windows app and the GPU tests");
        Check.True(settings.PromptMcp.Contains("Do not call propose_edit", StringComparison.Ordinal), "testing the MCP server calls the read-only tools only");
        Check.SequenceEqual(["claudeCode.open", "claudeCode.diagnoseCrash", "claudeCode.diagnoseStall", "claudeCode.testMcp", "claudeCode.buildAndTest", "claudeCode.find"],
            [.. ClaudeCodePlugin.Commands.Select(c => c.Name)], "the commands in the palette and the Run menu");
        Check.True(ClaudeReportAction.Instance is { Label: "Ask Claude Code", IsAvailable: false }, "Ask Claude Code, offered once Claude Code is found");
        Check.True(ClaudeEngine.Instance.Check("Claude Code") is { } inactive && inactive.Contains("not active", StringComparison.Ordinal), "the engine says when the plug-in is not active");

        var asked = new List<(string, string)>();
        EventHandler<AgentEngineAskEventArgs> take = (_, e) =>
        {
            asked.Add((e.Model, e.Task));
            e.Taken = true;
        };
        AgentEngines.Asked += take;
        try
        {
            Check.True(AgentEngines.Ask("Claude Code (claude-work)", onStall) && asked.Single() == ("Claude Code (claude-work)", onStall), "the prompt goes to the agent panel with the account's model");
        }
        finally
        {
            AgentEngines.Asked -= take;
        }

        Check.True(!AgentEngines.Ask("Claude Code", "x"), "with no agent panel, nobody takes it");
    }

    [Test]
    public static void TheSettingsAndTheAgentsChip()
    {
        var read = new Dictionary<string, string> { ["path"] = "C:/c/claude.exe", ["mcpWhenOff"] = "start", ["testMinutes"] = "5", ["approvals"] = "false" };
        var settings = ClaudeCodeSettings.From(new FakeSettings(read));
        Check.True(settings is { Path: "C:/c/claude.exe", McpWhenOff: McpWhenOff.Start, TestMinutes: 5, Approvals: false, PermissionMode: "acceptEdits" }, "the settings read");
        Check.Throws<ArgumentException>(() => ClaudeCodeSettings.From(new FakeSettings(new() { ["testMinutes"] = "zero" })), "a bad number names its variable");
        foreach (var key in (ReadOnlySpan<string>)["path", "accounts", "craftRepository", "idrakRepository"])
        {
            Check.True(ClaudeCodeSettings.Declared.Single(s => s.Key == key).MachineOnly, $"{key} is this machine's only");
        }

        var engine = new ListedEngine();
        AgentEngines.Register(engine);
        try
        {
            var agent = new ModelRole("Agent", "local-a", "loaded")
            {
                Key = ModelRoleKeys.Agent,
                ModelSetting = new ModelSettingField("CRAFT_PLUGIN_AI_AGENTMODEL", "Agent model"),
                LoadCommand = "ai.loadAgentModel",
            };
            var status = new ModelProviderStatus { Roles = [new ModelRole("Chat", "local-a", "loaded"), agent], Models = ["local-a"], LoadCommand = "ai.loadModel" };
            var picks = ShellText.ModelPicks(status, ModelRoleKeys.Agent);
            Check.True(picks.Any(p => p.Value == "Claude Code (claude-work)") && !ShellText.ModelPicks(status, ModelRoleKeys.Chat).Any(p => p.Value.StartsWith("Claude", StringComparison.Ordinal)),
                "the agent's chip lists the engine's models, one per account; the chat's does not");
            var write = ShellText.WriteOf(status, ModelRoleKeys.Agent, picks.First(p => p.Value == "Claude Code (claude-work)"));
            Check.True(write is { Variable: "CRAFT_PLUGIN_AI_AGENTMODEL", Value: "Claude Code (claude-work)", LoadCommand: null, IsEngine: true },
                "picking it sets the agent's model, loads nothing, and is kept per workspace");
            Check.True(AgentEngines.FindModel("claude code (CLAUDE-WORK)") == engine && AgentEngines.FindModel("local-a") is null, "which engine a model is, ignoring case");
        }
        finally
        {
            AgentEngines.Unregister(engine.Name);
        }
    }

    private static IEnumerable<string> Lines() =>
    [
        """{"type":"system","subtype":"init","session_id":"s-9","model":"the-model","mcp_servers":[{"name":"craft","status":"connected"}]}""",
        """{"type":"assistant","message":{"content":[{"type":"text","text":"Looking."},{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"dotnet build"}}]},"session_id":"s-9"}""",
        """{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"Build succeeded.\n0 errors","is_error":false}]}}""",
        """{"type":"result","subtype":"success","is_error":false,"result":"Fixed.","session_id":"s-9","num_turns":3,"duration_ms":4100,"total_cost_usd":0.12}""",
    ];

    private static void Write(string folder, string name, string text, int minutes)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc).AddMinutes(minutes));
    }

    private static Task<string> Call(Dictionary<string, Idrak.Abstraction.Generation.ITool> tools, string name, JsonObject arguments) =>
        tools[name].InvokeAsync(arguments, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));

    private sealed class FakeDiagnostics(IReadOnlyList<DiagnosticReport> reports) : IEditorDiagnostics
    {
        public string AppVersion => "test";

        public string? ReportFolder => "crashes";

        public IReadOnlyList<DiagnosticReport> Reports(string? kind, int max) => [.. reports.Where(r => kind is null || r.Kind == kind).Take(max)];

        public IReadOnlyList<string> TrailLines(int max) => [];

        public IReadOnlyList<string> OutputLines(int max) => [];

        public IReadOnlyList<TelemetryMeasure> Measures(int max) => [];
    }

    private sealed class FakeSettings(Dictionary<string, string> values) : IPluginSettings
    {
        public IReadOnlyList<PluginSettingValue> All => [];

        public string Get(string plugin, string key) => values.GetValueOrDefault(key) ?? "";
    }

    private sealed class ListedEngine : IAgentEngine
    {
        public string Name => "test-listed-engine";

        public string Summary => "lists models";

        public IReadOnlyList<string> Models { get; } = ["Claude Code", "Claude Code (claude-work)"];

        public Task<AgentEngineResult> RunAsync(AgentEngineRequest request, Action<AgentEngineEvent> report, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentEngineResult("", null));
    }

    private sealed class RecordingSandbox : ICommandSandbox
    {
        public List<(IReadOnlyList<string> Command, string Directory, CommandLimits Limits)> Commands { get; } = [];

        public string Name => "test-recording";

        public string Summary => "records the commands";

        public string? Check(CommandLimits limits) => null;

        public ValueTask<CommandResult> RunAsync(IReadOnlyList<string> command, string directory, CommandLimits limits, CancellationToken cancellationToken = default)
        {
            Commands.Add((command, directory, limits));
            return ValueTask.FromResult(new CommandResult(0, false, "2 passed, 0 failed", TimeSpan.FromSeconds(1), false));
        }
    }
}
