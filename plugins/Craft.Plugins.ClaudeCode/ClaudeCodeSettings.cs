// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Plugins.ClaudeCode;

/// <summary>What a headless run does when Craft's MCP server is off (<c>mcpWhenOff</c>).</summary>
public enum McpWhenOff
{
    /// <summary>Ask: start it, or run without it.</summary>
    Ask,

    /// <summary>Start it (its own command), then run.</summary>
    Start,

    /// <summary>Run without Craft's tools (and without its approvals: Claude Code then refuses what needs permission).</summary>
    Without,
}

/// <summary>The plug-in's settings (plug-in "claudeCode", variables <c>CRAFT_PLUGIN_CLAUDECODE_&lt;KEY&gt;</c>), on the settings page's Agent section.</summary>
public sealed record ClaudeCodeSettings
{
    /// <summary>The plug-in's name.</summary>
    public const string PluginName = "claudeCode";

    /// <summary>The Claude Code program (<c>path</c>, this machine's only): empty finds <c>claude</c> on PATH.</summary>
    public string Path { get; init; } = "";

    /// <summary>More arguments for every run (<c>args</c>, a command line).</summary>
    public string Arguments { get; init; } = "";

    /// <summary>The accounts (<c>accounts</c>, this machine's only): <c>name=folder</c> (its CLAUDE_CONFIG_DIR) or <c>name=VAR=value;VAR=value</c>, separated by '|'.</summary>
    public string Accounts { get; init; } = "";

    /// <summary>The account commands use when the agent's model is not a Claude Code one (<c>account</c>; empty: plain <c>claude</c>).</summary>
    public string Account { get; init; } = "";

    /// <summary>Whether accounts are found in the PowerShell profiles and Windows Terminal's settings, read as text (<c>detectAccounts</c>).</summary>
    public bool DetectAccounts { get; init; } = true;

    /// <summary>Claude Code's <c>--permission-mode</c> for a run on the copy (<c>permissionMode</c>: acceptEdits, default, plan, bypassPermissions).</summary>
    public string PermissionMode { get; init; } = "acceptEdits";

    /// <summary>Whether Claude Code asks Craft (a question to the user) before what needs permission, through Craft's MCP tool (<c>approvals</c>).</summary>
    public bool Approvals { get; init; } = true;

    /// <summary>What a run does when Craft's MCP server is off (<c>mcpWhenOff</c>: ask, start, without).</summary>
    public McpWhenOff McpWhenOff { get; init; } = McpWhenOff.Ask;

    /// <summary>The most characters a diagnostic tool returns (<c>toolOutputCharacters</c>).</summary>
    public int ToolOutputCharacters { get; init; } = 16_000;

    /// <summary>The command sandbox <c>run_tests</c> goes through (<c>testSandbox</c>; it asks the user first).</summary>
    public string TestSandbox { get; init; } = "approve-each";

    /// <summary>Minutes a <c>run_tests</c> command may take (<c>testMinutes</c>).</summary>
    public int TestMinutes { get; init; } = 20;

    /// <summary>Craft's repository (<c>craftRepository</c>, this machine's only); empty: found above the app's folder.</summary>
    public string CraftRepository { get; init; } = "";

    /// <summary>Idrak's repository (<c>idrakRepository</c>, this machine's only); empty: the Idrak folder beside Craft's.</summary>
    public string IdrakRepository { get; init; } = "";

    /// <summary>The prompt of "diagnose the last crash" (<c>promptCrash</c>).</summary>
    public string PromptCrash { get; init; } = DefaultPromptCrash;

    /// <summary>The prompt of "diagnose the last stall" (<c>promptStall</c>).</summary>
    public string PromptStall { get; init; } = DefaultPromptStall;

    /// <summary>The prompt of "test Craft's MCP server" (<c>promptMcp</c>).</summary>
    public string PromptMcp { get; init; } = DefaultPromptMcp;

    /// <summary>The prompt of "build and test on this machine" (<c>promptBuild</c>).</summary>
    public string PromptBuild { get; init; } = DefaultPromptBuild;

    /// <summary>The default crash prompt: {report}, {kind}, {craft}, {idrak}, {workspace} are filled in.</summary>
    public const string DefaultPromptCrash =
        "Craft, the code editor you are connected to over MCP as the server 'craft', caught a failure ({kind}). Its report is {report}. "
        + "Read it with the craft_read_report tool, the crash trail's last lines with craft_trail and Output's with craft_output, and the "
        + "app's state with craft_app_state. Then find the cause in Craft's source ({craft}) or Idrak's ({idrak}), explain it, and propose "
        + "the fix. Never run the full test suite: run_tests takes a CRAFT_FILTER of the groups the fix touches.";

    /// <summary>The default stall prompt.</summary>
    public const string DefaultPromptStall =
        "Craft's window was blocked (a UI-thread stall the watchdog saw). The stall report is {report}. Read it with craft_read_report, "
        + "the crash trail around it with craft_trail, the telemetry with craft_telemetry and Output with craft_output. Find the work that "
        + "ran on the UI thread in Craft's source ({craft}) or Idrak's ({idrak}), and propose how to move it off the UI thread. Never run "
        + "the full test suite: run_tests takes a CRAFT_FILTER.";

    /// <summary>The default prompt that tests the MCP server.</summary>
    public const string DefaultPromptMcp =
        "Test Craft's MCP server (the server 'craft'): list its tools, then call each read-only one once (craft_app_state, craft_reports, "
        + "craft_read_report with name latest, craft_trail with 50 lines, craft_output with 50 lines, craft_telemetry, craft_problems, "
        + "open_documents, get_diagnostics), check every answer is bounded and makes sense, and report each tool that failed and why. "
        + "Do not call propose_edit, open_file or run_tests.";

    /// <summary>The default prompt that builds and tests on this machine (its CPU and GPU).</summary>
    public const string DefaultPromptBuild =
        "Build and test Craft and Idrak on this machine, with its own hardware. In {craft}: run `dotnet build -c Release tests\\Craft.Tests`, "
        + "then `dotnet run -c Release --no-build --project tests\\Craft.Tests -- --progress --failures-only --failures-file test-output\\fails.txt` "
        + "with CRAFT_FILTER set to the groups to check (never the full suite), and the Windows app: `dotnet build -c Release src\\Craft.App "
        + "-f net10.0-windows10.0.19041.0`. In {idrak}: run `dotnet build -c Release tests\\Idrak.Tests`, then Idrak's GPU tests: "
        + "`dotnet run -c Release --no-build --project tests\\Idrak.Tests -- --progress --failures-only --failures-file test-output\\fails.txt` "
        + "with IDRAK_DEVICES set to this machine's GPU device (as Idrak's tests name it; cpu for the CPU) and IDRAK_FILTER set to the "
        + "groups to check. Report each failure with its cause, and what you measured.";

    /// <summary>The settings it declares.</summary>
    public static IReadOnlyList<PluginSetting> Declared { get; } =
    [
        new("path", "", "The Claude Code program (claude.exe, or the claude.cmd npm installs). Empty: claude on PATH. This machine's only.") { MachineOnly = true },
        new("args", "", "More arguments for every Claude Code run, as a command line (its documented flags: --model, --add-dir …)."),
        new("accounts", "", "Your Claude Code accounts: name=folder (that account's CLAUDE_CONFIG_DIR) or name=VAR=value;VAR=value, separated by '|'. Each shows at the agent's model chip as Claude Code (name). This machine's only.") { MachineOnly = true },
        new("account", "", "The account the Claude Code commands use when the agent's model is not a Claude Code one; empty: plain claude."),
        new("detectAccounts", "true", "Find accounts in your PowerShell profiles (functions named claude-*) and Windows Terminal's profiles named claude*, read as text, never run."),
        new("permissionMode", "acceptEdits", "Claude Code's --permission-mode for a run (it works on a copy of the folder; every change still comes to you as a diff)."),
        new("approvals", "true", "Ask you before Claude Code does what needs permission (a command, a web fetch), through Craft's MCP tool claude_code_approve."),
        new("mcpWhenOff", "ask", "When Craft's MCP server is off: ask, start (its own command) or without (no Craft tools and no approvals).") { Shape = SettingShape.Choice(SettingSections.Agent, "When Craft's MCP server is off", ["ask", "start", "without"], "Claude Code") },
        new("toolOutputCharacters", "16000", "The most characters a diagnostic MCP tool returns (a report, the trail, Output)."),
        new("testSandbox", "approve-each", "The command sandbox run_tests goes through (approve-each asks you first)."),
        new("testMinutes", "20", "Minutes a run_tests command may take."),
        new("craftRepository", "", "Craft's repository (run_tests and the prompts). Empty: found above the app's folder. This machine's only.") { MachineOnly = true },
        new("idrakRepository", "", "Idrak's repository (the prompts). Empty: the Idrak folder beside Craft's. This machine's only.") { MachineOnly = true },
        new("promptCrash", DefaultPromptCrash, "The prompt of 'Claude Code: diagnose the last crash' and of Ask Claude Code on a failure: {report}, {kind}, {craft}, {idrak}, {workspace} are filled in."),
        new("promptStall", DefaultPromptStall, "The prompt of 'Claude Code: diagnose the last stall' and of Ask Claude Code on a stall."),
        new("promptMcp", DefaultPromptMcp, "The prompt of 'Claude Code: test Craft's MCP server'."),
        new("promptBuild", DefaultPromptBuild, "The prompt of 'Claude Code: build and test on this machine'."),
    ];

    /// <summary>The settings from the host's.</summary>
    /// <exception cref="ArgumentException">A value that does not parse, naming its variable.</exception>
    public static ClaudeCodeSettings From(IPluginSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var defaults = new ClaudeCodeSettings();
        string Get(string key) => settings.Get(PluginName, key).Trim();
        string Text(string key, string fallback) => Get(key) is { Length: > 0 } value ? value : fallback;
        int Whole(string key, int fallback, int min, int max)
        {
            string text = Get(key);
            if (text.Length == 0)
            {
                return fallback;
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number >= min && number <= max
                ? number
                : throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{text}': it takes a whole number from {min} to {max}.", key);
        }

        bool Flag(string key, bool fallback)
        {
            string text = Get(key);
            return text.Length == 0 ? fallback
                : text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1" ? true
                : text.Equals("false", StringComparison.OrdinalIgnoreCase) || text == "0" ? false
                : throw new ArgumentException($"{PluginSetting.VariableName(PluginName, key)} is '{text}': it takes true or false.", key);
        }

        string when = Get("mcpWhenOff");
        return new ClaudeCodeSettings
        {
            Path = Get("path"),
            Arguments = Get("args"),
            Accounts = Get("accounts"),
            Account = Get("account"),
            DetectAccounts = Flag("detectAccounts", defaults.DetectAccounts),
            PermissionMode = Text("permissionMode", defaults.PermissionMode),
            Approvals = Flag("approvals", defaults.Approvals),
            McpWhenOff = when.Length == 0 ? defaults.McpWhenOff
                : Enum.TryParse<McpWhenOff>(when, ignoreCase: true, out var parsed) ? parsed
                : throw new ArgumentException($"{PluginSetting.VariableName(PluginName, "mcpWhenOff")} is '{when}': it takes ask, start or without.", "mcpWhenOff"),
            ToolOutputCharacters = Whole("toolOutputCharacters", defaults.ToolOutputCharacters, 500, 10_000_000),
            TestSandbox = Text("testSandbox", defaults.TestSandbox),
            TestMinutes = Whole("testMinutes", defaults.TestMinutes, 1, 24 * 60),
            CraftRepository = Get("craftRepository"),
            IdrakRepository = Get("idrakRepository"),
            PromptCrash = Text("promptCrash", defaults.PromptCrash),
            PromptStall = Text("promptStall", defaults.PromptStall),
            PromptMcp = Text("promptMcp", defaults.PromptMcp),
            PromptBuild = Text("promptBuild", defaults.PromptBuild),
        };
    }

    /// <summary>Craft's repository: the setting, else the first folder above <paramref name="start"/> (the app's) holding Craft.slnx and tests/Craft.Tests; null when none.</summary>
    public string? CraftRepositoryFrom(string start)
    {
        if (CraftRepository.Length > 0)
        {
            return Directory.Exists(CraftRepository) ? System.IO.Path.GetFullPath(CraftRepository) : null;
        }

        for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(folder.FullName, "Craft.slnx")) && Directory.Exists(System.IO.Path.Combine(folder.FullName, "tests", "Craft.Tests")))
            {
                return folder.FullName;
            }
        }

        return null;
    }

    /// <summary>Idrak's repository: the setting, else the Idrak folder beside <paramref name="craft"/>; null when none.</summary>
    public string? IdrakRepositoryFrom(string? craft)
    {
        if (IdrakRepository.Length > 0)
        {
            return System.IO.Path.GetFullPath(IdrakRepository);
        }

        return craft is null || Directory.GetParent(craft) is not { } parent ? null : System.IO.Path.Combine(parent.FullName, "Idrak");
    }

    /// <summary>
    /// <paramref name="template"/> with its places filled: {report}, {kind}, {craft}, {idrak}, {workspace} (each "not known"
    /// when missing); one line, so it goes to Claude Code as one prompt.
    /// </summary>
    public static string Fill(string template, string? report, string? kind, string? craft, string? idrak, string? workspace)
    {
        ArgumentNullException.ThrowIfNull(template);
        return template
            .Replace("{report}", report ?? "(none found)", StringComparison.Ordinal)
            .Replace("{kind}", kind ?? "a failure", StringComparison.Ordinal)
            .Replace("{craft}", craft ?? "Craft's repository (not found on this machine)", StringComparison.Ordinal)
            .Replace("{idrak}", idrak ?? "Idrak's repository (not found on this machine)", StringComparison.Ordinal)
            .Replace("{workspace}", workspace ?? "(no folder open)", StringComparison.Ordinal);
    }
}
