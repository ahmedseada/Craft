// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Craft.Plugins.ClaudeCode;

/// <summary>
/// A Claude Code account: a name and the variables a run takes for it (its <c>CLAUDE_CONFIG_DIR</c>, so its own sign-in
/// and subscription), from the settings or found in the user's own launchers.
/// </summary>
/// <param name="Name">Its name ("claude-work").</param>
/// <param name="Environment">The variables a run sets for it.</param>
/// <param name="Source">Where it was found ("settings", a profile's path, "Windows Terminal").</param>
public sealed record ClaudeAccount(string Name, IReadOnlyDictionary<string, string> Environment, string Source)
{
    /// <summary>Why a run cannot use it (its launcher sets nothing Craft can read), with what to do; null when it can.</summary>
    public string? Problem { get; init; }

    /// <summary>The model name the agent's chip shows for it ("Claude Code (claude-work)").</summary>
    public string Model => ClaudeAccounts.ModelOf(Name);
}

/// <summary>
/// The accounts (the owner's two subscriptions, run as PowerShell functions or Windows Terminal profiles named claude-*):
/// the setting's (<c>name=folder</c>, or <c>name=VAR=value;VAR=value</c>, separated by '|'), and, read as text and never
/// run, the <c>claude-*</c> functions of the PowerShell profiles (what they assign to <c>$env:…</c>) and Windows
/// Terminal's profiles named <c>claude*</c> (their command line's variables, or the function they call).
/// </summary>
public static partial class ClaudeAccounts
{
    /// <summary>The plain model: <c>claude</c> with no account's variables.</summary>
    public const string PlainModel = "Claude Code";

    /// <summary>The variable that points Claude Code at an account's own configuration (its sign-in).</summary>
    public const string ConfigVariable = "CLAUDE_CONFIG_DIR";

    /// <summary>The model name of account <paramref name="name"/>.</summary>
    public static string ModelOf(string name) => $"{PlainModel} ({name})";

    /// <summary>The account a model's name names ("Claude Code (claude-work)" gives "claude-work"), or null for the plain model or another.</summary>
    public static string? AccountOf(string? model)
    {
        if (model is null)
        {
            return null;
        }

        var text = model.Trim();
        return text.StartsWith(PlainModel + " (", StringComparison.OrdinalIgnoreCase) && text.EndsWith(')') ? text[(PlainModel.Length + 2)..^1].Trim() : null;
    }

    /// <summary>The setting's accounts: <c>name=folder</c> (its CLAUDE_CONFIG_DIR) or <c>name=VAR=value;VAR=value</c>, separated by '|'.</summary>
    public static IReadOnlyList<ClaudeAccount> Parse(string setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        var accounts = new List<ClaudeAccount>();
        foreach (var entry in setting.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = entry.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                throw new ArgumentException($"CRAFT_PLUGIN_CLAUDECODE_ACCOUNTS: '{entry}' has no name: write name=folder or name=VAR=value;VAR=value.", nameof(setting));
            }

            string name = entry[..equals].Trim();
            string value = entry[(equals + 1)..].Trim();
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (value.Contains('=', StringComparison.Ordinal))
            {
                foreach (var pair in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    int at = pair.IndexOf('=', StringComparison.Ordinal);
                    if (at > 0)
                    {
                        environment[pair[..at].Trim()] = pair[(at + 1)..].Trim().Trim('"');
                    }
                }
            }
            else if (value.Length > 0)
            {
                environment[ConfigVariable] = value.Trim('"');
            }

            accounts.Add(new ClaudeAccount(name, environment, "settings"));
        }

        return accounts;
    }

    /// <summary>
    /// The <c>claude-*</c> functions of a PowerShell profile's <paramref name="text"/> (and the aliases that name them), each
    /// with the variables its body assigns (<c>$env:NAME = …</c>, <c>[Environment]::SetEnvironmentVariable('NAME', …)</c>),
    /// their values expanded where they are plain (<c>$HOME</c>, <c>$env:NAME</c>, <c>~</c>, <c>Join-Path A B</c>); a
    /// function that assigns nothing readable is listed with its problem. Never run.
    /// </summary>
    public static IReadOnlyList<ClaudeAccount> FromPowerShellProfile(string text, string source, Func<string, string?> variable)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(variable);
        var found = new List<ClaudeAccount>();
        foreach (Match match in FunctionPattern().Matches(text))
        {
            string name = match.Groups["name"].Value;
            int open = text.IndexOf('{', match.Index + match.Length - 1);
            if (open < 0)
            {
                continue;
            }

            string body = Body(text, open);
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match assignment in AssignmentPattern().Matches(body))
            {
                if (Expand(assignment.Groups["value"].Value, variable) is { } value)
                {
                    environment[assignment.Groups["var"].Value] = value;
                }
            }

            foreach (Match assignment in SetVariablePattern().Matches(body))
            {
                if (Expand(assignment.Groups["value"].Value, variable) is { } value)
                {
                    environment[assignment.Groups["var"].Value] = value;
                }
            }

            found.Add(new ClaudeAccount(name, environment, source)
            {
                Problem = environment.Count == 0
                    ? $"The function {name} in {source} sets no variable Craft can read (such as $env:{ConfigVariable}). Name its folder in CRAFT_PLUGIN_CLAUDECODE_ACCOUNTS ({name}=C:\\path\\to\\its\\config)."
                    : null,
            });
        }

        // An alias of a function (Set-Alias claude-work Start-ClaudeWork): the function's variables under the alias's name.
        foreach (Match alias in AliasPattern().Matches(text))
        {
            string name = alias.Groups["name"].Value;
            string target = alias.Groups["target"].Value;
            if (found.Exists(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            int at = text.IndexOf("function " + target, StringComparison.OrdinalIgnoreCase);
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (at >= 0 && text.IndexOf('{', at) is var open and >= 0)
            {
                foreach (Match assignment in AssignmentPattern().Matches(Body(text, open)))
                {
                    if (Expand(assignment.Groups["value"].Value, variable) is { } value)
                    {
                        environment[assignment.Groups["var"].Value] = value;
                    }
                }
            }

            found.Add(new ClaudeAccount(name, environment, source)
            {
                Problem = environment.Count == 0 ? $"The alias {name} in {source} names {target}, which sets no variable Craft can read: name its folder in CRAFT_PLUGIN_CLAUDECODE_ACCOUNTS." : null,
            });
        }

        return found;
    }

    /// <summary>
    /// Windows Terminal's profiles named <c>claude*</c> in its settings <paramref name="json"/> (comments allowed): each with
    /// its <c>environment</c>, the variables its command line sets (<c>set NAME=…</c>, <c>$env:NAME=…</c>), else the
    /// variables of the <c>claude-*</c> function it calls (from <paramref name="functions"/>).
    /// </summary>
    public static IReadOnlyList<ClaudeAccount> FromWindowsTerminal(string json, IReadOnlyList<ClaudeAccount> functions, Func<string, string?> variable)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(variable);
        var found = new List<ClaudeAccount>();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            return found;
        }

        using (document)
        {
            var root = document.RootElement;
            JsonElement list = default;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("profiles", out var profiles))
            {
                list = profiles.ValueKind == JsonValueKind.Array ? profiles : profiles.ValueKind == JsonValueKind.Object && profiles.TryGetProperty("list", out var inner) ? inner : default;
            }

            if (list.ValueKind != JsonValueKind.Array)
            {
                return found;
            }

            foreach (var profile in list.EnumerateArray())
            {
                if (profile.ValueKind != JsonValueKind.Object || !profile.TryGetProperty("name", out var nameElement) || nameElement.GetString() is not { } name
                    || !name.StartsWith("claude", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (profile.TryGetProperty("environment", out var variables) && variables.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in variables.EnumerateObject())
                    {
                        if (pair.Value.GetString() is { } value && Expand(value, variable) is { } expanded)
                        {
                            environment[pair.Name] = expanded;
                        }
                    }
                }

                string command = profile.TryGetProperty("commandline", out var line) ? line.GetString() ?? "" : "";
                foreach (Match set in CmdSetPattern().Matches(command))
                {
                    if (Expand(set.Groups["value"].Value, variable) is { } value)
                    {
                        environment[set.Groups["var"].Value] = value;
                    }
                }

                foreach (Match assignment in AssignmentPattern().Matches(command))
                {
                    if (Expand(assignment.Groups["value"].Value, variable) is { } value)
                    {
                        environment[assignment.Groups["var"].Value] = value;
                    }
                }

                if (environment.Count == 0 && ClaudeFunctionPattern().Match(command) is { Success: true } called
                    && functions.FirstOrDefault(f => f.Name.Equals(called.Value, StringComparison.OrdinalIgnoreCase)) is { } function)
                {
                    foreach (var (key, value) in function.Environment)
                    {
                        environment[key] = value;
                    }
                }

                found.Add(new ClaudeAccount(name, environment, "Windows Terminal")
                {
                    Problem = environment.Count == 0
                        ? $"Windows Terminal's profile {name} sets no variable Craft can read: name its folder in CRAFT_PLUGIN_CLAUDECODE_ACCOUNTS ({name}=C:\\path\\to\\its\\config)."
                        : null,
                });
            }
        }

        return found;
    }

    /// <summary>The PowerShell profiles to read under <paramref name="documents"/> (PowerShell 7's, then Windows PowerShell's).</summary>
    public static IReadOnlyList<string> ProfilePaths(string documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return
        [
            Path.Combine(documents, "PowerShell", "Microsoft.PowerShell_profile.ps1"),
            Path.Combine(documents, "PowerShell", "profile.ps1"),
            Path.Combine(documents, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1"),
            Path.Combine(documents, "WindowsPowerShell", "profile.ps1"),
        ];
    }

    /// <summary>Windows Terminal's settings files under <paramref name="localAppData"/> (the store's, its preview's, the unpackaged one).</summary>
    public static IReadOnlyList<string> TerminalSettingsPaths(string localAppData)
    {
        ArgumentNullException.ThrowIfNull(localAppData);
        return
        [
            Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminal_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(localAppData, "Packages", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe", "LocalState", "settings.json"),
            Path.Combine(localAppData, "Microsoft", "Windows Terminal", "settings.json"),
        ];
    }

    /// <summary>
    /// The accounts found on this machine, read as text off the UI thread: the PowerShell profiles' functions, then Windows
    /// Terminal's profiles (one per name, the first found). A file that cannot be read is passed over.
    /// </summary>
    public static IReadOnlyList<ClaudeAccount> Detect(string documents, string localAppData, Func<string, string?> variable, Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var found = new List<ClaudeAccount>();
        foreach (var file in ProfilePaths(documents))
        {
            if (read(file) is { } text)
            {
                found.AddRange(FromPowerShellProfile(text, file, variable));
            }
        }

        var functions = found.ToArray();
        foreach (var file in TerminalSettingsPaths(localAppData))
        {
            if (read(file) is { } json)
            {
                found.AddRange(FromWindowsTerminal(json, functions, variable));
            }
        }

        return Merge([], found);
    }

    /// <summary>The setting's accounts, then the found ones of other names (a name once: the setting's wins, then the first found that can run).</summary>
    public static IReadOnlyList<ClaudeAccount> Merge(IReadOnlyList<ClaudeAccount> configured, IReadOnlyList<ClaudeAccount> detected)
    {
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentNullException.ThrowIfNull(detected);
        var merged = new List<ClaudeAccount>(configured);
        foreach (var account in detected.OrderBy(a => a.Problem is null ? 0 : 1))
        {
            if (!merged.Exists(a => a.Name.Equals(account.Name, StringComparison.OrdinalIgnoreCase)))
            {
                merged.Add(account);
            }
        }

        return merged;
    }

    /// <summary>A file's text, or null when it is missing or cannot be read (the detection's reader).</summary>
    public static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The function's body: from its '{' to the matching '}', braces inside quotes passed over.
    private static string Body(string text, int open)
    {
        int depth = 0;
        char quote = '\0';
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return text[(open + 1)..i];
            }
        }

        return text[(open + 1)..];
    }

    // A value as PowerShell would read it, where that is plain: quotes off, $HOME, $env:NAME, ${env:NAME}, a leading ~, and
    // Join-Path A B; null for what only running it could tell (another variable, a command's output).
    private static string? Expand(string raw, Func<string, string?> variable)
    {
        var value = raw.Trim().TrimEnd(';').Trim();
        if (JoinPathPattern().Match(value) is { Success: true } join)
        {
            return Expand(join.Groups["a"].Value, variable) is { } a && Expand(join.Groups["b"].Value, variable) is { } b ? Path.Combine(a, b) : null;
        }

        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1];                                    // single quotes: taken as written
        }

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        string home = variable("USERPROFILE") ?? variable("HOME") ?? "";
        if (value.StartsWith('~'))
        {
            value = home + value[1..];
        }

        bool unknown = false;
        value = VariablePattern().Replace(value, m =>
        {
            string name = m.Groups["env"].Success ? m.Groups["env"].Value : m.Groups["plain"].Value;
            string? resolved = m.Groups["env"].Success ? variable(name) : name.Equals("HOME", StringComparison.OrdinalIgnoreCase) ? home : null;
            unknown |= resolved is null;
            return resolved ?? m.Value;
        });
        return unknown || value.Length == 0 || value.Contains('(', StringComparison.Ordinal) ? null : value;
    }

    [GeneratedRegex(@"(?im)^\s*function\s+(?:global:|script:)?(?<name>claude-[A-Za-z0-9_-]+)\s*(?:\([^)]*\))?\s*\{", RegexOptions.CultureInvariant)]
    private static partial Regex FunctionPattern();

    [GeneratedRegex(@"\$env:(?<var>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>Join-Path\s+\S+\s+\S+|""[^""]*""|'[^']*'|[^\s;|&]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AssignmentPattern();

    [GeneratedRegex(@"\[(?:System\.)?Environment\]::SetEnvironmentVariable\(\s*['""](?<var>[A-Za-z_][A-Za-z0-9_]*)['""]\s*,\s*(?<value>""[^""]*""|'[^']*')", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SetVariablePattern();

    [GeneratedRegex(@"(?im)^\s*(?:Set|New)-Alias\s+(?:-Name\s+)?(?<name>claude-[A-Za-z0-9_-]+)\s+(?:-Value\s+)?(?<target>[A-Za-z0-9_-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AliasPattern();

    [GeneratedRegex(@"(?i)\bset\s+""?(?<var>[A-Za-z_][A-Za-z0-9_]*)=(?<value>[^""&]+)""?", RegexOptions.CultureInvariant)]
    private static partial Regex CmdSetPattern();

    [GeneratedRegex(@"(?i)\bclaude-[A-Za-z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeFunctionPattern();

    [GeneratedRegex(@"(?i)^Join-Path\s+(?<a>""[^""]*""|'[^']*'|\S+)\s+(?<b>""[^""]*""|'[^']*'|\S+)$", RegexOptions.CultureInvariant)]
    private static partial Regex JoinPathPattern();

    [GeneratedRegex(@"\$\{env:(?<env>[A-Za-z_][A-Za-z0-9_]*)\}|\$env:(?<env>[A-Za-z_][A-Za-z0-9_]*)|\$(?<plain>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();
}
