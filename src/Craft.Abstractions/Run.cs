// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Craft.Abstractions;

/// <summary>What can be done with a run target (X part 6: the Run view's buttons).</summary>
[Flags]
public enum RunActions
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Run it (a program, a launch profile, a script).</summary>
    Run = 1,

    /// <summary>Build it.</summary>
    Build = 2,

    /// <summary>Run its tests.</summary>
    Test = 4,

    /// <summary>Publish it.</summary>
    Publish = 8,
}

/// <summary>Something a workspace can run or build, as a run provider found it.</summary>
/// <param name="Provider">The provider that found it (its name in <see cref="RunProviders"/>).</param>
/// <param name="Id">Its id, unique within the provider and stable across finds (its file relative to the root, a profile after '#').</param>
/// <param name="Title">What the Run view shows ("Craft.App", "build.ps1").</param>
/// <param name="File">Its file, relative to the workspace's root ('/' between folders).</param>
/// <param name="Actions">What can be done with it.</param>
public sealed record RunTarget(string Provider, string Id, string Title, string File, RunActions Actions)
{
    /// <summary>A quieter line beside it ("Exe · net10.0", "launch profile · https://localhost:5001"), or null.</summary>
    public string? Detail { get; init; }

    /// <summary>The arguments it runs with (a launch profile's), or null.</summary>
    public string? Arguments { get; init; }

    /// <summary>The variables it runs with (a launch profile's), over the inherited ones.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = RunConfiguration.NoEnvironment;

    /// <summary>The address it serves (a launch profile's application URL), or null.</summary>
    public string? Url { get; init; }

    /// <summary>The program it runs itself (a launch profile's executable), or null: the provider's own command.</summary>
    public string? Program { get; init; }

    /// <summary>
    /// Whether its run may read standard input (a console program, a script): with <c>CRAFT_RUN_INTERACTIVE</c> "auto" (and
    /// the configuration's <see cref="RunInteractive.Auto"/>) such a run goes to a terminal tab that takes the keyboard (X
    /// part 6b). False for what serves or shows a window (a web project, a worker, a windowed app).
    /// </summary>
    public bool ReadsInput { get; init; }

    /// <summary>What F5 does with it: <see cref="RunActions.Run"/> when it runs, else the first action it has.</summary>
    public RunActions DefaultAction => (Actions & RunActions.Run) != 0 ? RunActions.Run
        : (Actions & RunActions.Build) != 0 ? RunActions.Build
        : (Actions & RunActions.Test) != 0 ? RunActions.Test
        : Actions;
}

/// <summary>A command a run starts: the program and its arguments as separate words (no shell), its folder and variables.</summary>
/// <param name="Command">The program, then its arguments; empty when it cannot run here (<see cref="Problem"/>).</param>
/// <param name="Folder">The folder it runs in (a full path).</param>
public sealed record RunCommand(IReadOnlyList<string> Command, string Folder)
{
    /// <summary>The variables set for it, over the inherited ones.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = RunConfiguration.NoEnvironment;

    /// <summary>Why it cannot run here (a program that is not installed, a script of another system), or null.</summary>
    public string? Problem { get; init; }

    /// <summary>The command as a line the user reads: each word quoted when it holds a space or a quote.</summary>
    public string Line => RunCommandLine.Join(Command);

    /// <summary>A command that cannot run, saying why.</summary>
    public static RunCommand Unavailable(string why, string folder) => new([], folder) { Problem = why };
}

/// <summary>Whether a run takes the keyboard (X part 6b): its program's input, output and error in a terminal tab, or its output only.</summary>
public enum RunInteractive
{
    /// <summary>As <c>CRAFT_RUN_INTERACTIVE</c> says; its "auto": when the target reads input (<see cref="RunTarget.ReadsInput"/>).</summary>
    Auto,

    /// <summary>Always: a run's tab takes the keyboard.</summary>
    On,

    /// <summary>Never: the run's output only, its input closed.</summary>
    Off,
}

/// <summary>
/// A run configuration (the Run view's, kept in the workspace's <c>.craft/run.json</c>): a target of a provider, what to do
/// with it, and how.
/// </summary>
/// <param name="Name">Its name, unique in the workspace.</param>
/// <param name="Provider">The provider of its target.</param>
/// <param name="Target">The target's <see cref="RunTarget.Id"/>.</param>
public sealed record RunConfiguration(string Name, string Provider, string Target)
{
    /// <summary>No variables.</summary>
    public static IReadOnlyDictionary<string, string> NoEnvironment { get; } = new Dictionary<string, string>();

    /// <summary>What it does.</summary>
    public RunActions Action { get; init; } = RunActions.Run;

    /// <summary>More arguments, as a command line ("--port 5000 \"a b\"").</summary>
    public string Arguments { get; init; } = "";

    /// <summary>Variables set for it, over the target's and the inherited ones.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = NoEnvironment;

    /// <summary>The folder it runs in, relative to the workspace's root; null: the provider's choice.</summary>
    public string? Folder { get; init; }

    /// <summary>Whether a run builds the target first (stopping there when the build fails).</summary>
    public bool BuildFirst { get; init; }

    /// <summary>Whether its run takes the keyboard (X part 6b; only a run: a build, a test or a publish shows its output only).</summary>
    public RunInteractive Interactive { get; init; }

    /// <inheritdoc />
    public bool Equals(RunConfiguration? other) =>
        other is not null && Name == other.Name && Provider == other.Provider && Target == other.Target && Action == other.Action && Arguments == other.Arguments
        && Folder == other.Folder && BuildFirst == other.BuildFirst && Interactive == other.Interactive && Environment.Count == other.Environment.Count
        && Environment.All(pair => other.Environment.TryGetValue(pair.Key, out var value) && value == pair.Value);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, Provider, Target, Action, Arguments, Folder, BuildFirst, Environment.Count);
}

/// <summary>The workspace a provider looks through: its root and its files (relative, '/' between folders), ignored folders and <c>.gitignore</c>'s left out.</summary>
/// <param name="Root">The workspace's folder (a full path).</param>
/// <param name="Files">Its files, relative to the root.</param>
public sealed record RunWorkspace(string Root, IReadOnlyList<string> Files);

/// <summary>A problem a run's output reported (a compiler's error), for Problems.</summary>
/// <param name="File">The file's full path.</param>
/// <param name="Line">Its line, from 1.</param>
/// <param name="Column">Its column, from 1.</param>
/// <param name="Severity">How bad.</param>
/// <param name="Code">Its code ("CS1002"), or "".</param>
/// <param name="Message">What it says.</param>
public sealed record RunProblem(string File, int Line, int Column, DiagnosticSeverity Severity, string Code, string Message);

/// <summary>
/// What a workspace can run and build, by one kind of project, registered in <see cref="RunProviders"/>: the library's
/// "scripts" (PowerShell, command and shell scripts at the root and in a <c>scripts</c> folder), the C# plug-in's
/// "dotnet" (projects, solutions, launch profiles), a web plug-in's npm scripts. The Run view asks every provider off
/// the UI thread and again when a file it <see cref="Watches"/> changes; every command runs through a registered
/// sandbox after the user's approval, never by the provider.
/// </summary>
public interface IRunProvider : IKind
{
    /// <summary>Its title in the Run view ("C# (.NET)", "Scripts").</summary>
    string Title { get; }

    /// <summary>Whether a file of this name (a project, a launch settings file) changes what <see cref="Find"/> gives.</summary>
    bool Watches(ReadOnlySpan<char> fileName);

    /// <summary>What <paramref name="workspace"/> can run or build (called off the UI thread; it may read files).</summary>
    IReadOnlyList<RunTarget> Find(RunWorkspace workspace, CancellationToken cancellationToken = default);

    /// <summary>
    /// The command that does <paramref name="action"/> with <paramref name="target"/> in the workspace <paramref name="root"/>,
    /// with <paramref name="configuration"/>'s arguments, variables and folder when given; one whose
    /// <see cref="RunCommand.Problem"/> says why when it cannot run here.
    /// </summary>
    RunCommand Command(RunTarget target, RunActions action, RunConfiguration? configuration, string root);

    /// <summary>
    /// Reads a line of a run's output as a problem (a compiler's error) when it is one; <paramref name="folder"/> is the run's
    /// folder (a relative file is taken from it). By default MSBuild's and the compilers' form (<see cref="RunProblems.TryParse"/>).
    /// </summary>
    bool TryParseProblem(ReadOnlySpan<char> line, string folder, [NotNullWhen(true)] out RunProblem? problem) => RunProblems.TryParse(line, folder, out problem);

    /// <summary>
    /// The command that runs only the test <paramref name="testName"/> (fully qualified) of <paramref name="target"/> (one
    /// of this provider's targets that tests) in the workspace <paramref name="root"/>, or null when this provider cannot
    /// pick one test (the default). X part 9: a code lens's Run test (contract 1.1).
    /// </summary>
    RunCommand? TestCommand(RunTarget target, string testName, string root) => null;
}

/// <summary>
/// The run providers by name (ignoring case), the newest first: each plug-in's, then the library's <see cref="Scripts"/>.
/// An unknown name fails with "not registered".
/// </summary>
public static class RunProviders
{
    /// <summary>PowerShell, command and shell scripts at the root and in a <c>scripts</c> folder (the library's).</summary>
    public const string Scripts = "scripts";

    private static readonly KindTable<IRunProvider> Table = BuiltIn();

    /// <summary>Raised when a provider is registered or unregistered (on the registering thread): the Run view looks again.</summary>
    public static event EventHandler? Changed;

    private static KindTable<IRunProvider> BuiltIn()
    {
        // Newest first: a plug-in's providers (a project's runners) come before the library's scripts, so F5 with no
        // default picks a project.
        var table = new KindTable<IRunProvider>(nameof(RunProviders), "Run provider", "add it with RunProviders.Register",
            comparer: StringComparer.OrdinalIgnoreCase, newestFirst: true, unguarded: "each provider lists its own targets; none stands in for another");
        table.RegisterDefault(new ScriptRunProvider());
        return table;
    }

    /// <summary>Registers <paramref name="provider"/> under its name (over the library's of that name, which stays behind it).</summary>
    public static void Register(IRunProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Table.Register(provider, System.Reflection.Assembly.GetCallingAssembly());
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Removes the app's provider of that name (the library's, if any, answers again).</summary>
    public static bool Unregister(string name)
    {
        bool removed = Table.Unregister(name);
        if (removed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered providers, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<IRunProvider> All => Table.Kinds;

    /// <summary>The provider of that name, or null.</summary>
    public static IRunProvider? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The provider of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IRunProvider Get(ReadOnlySpan<char> name) => Table.Get(name);
}

/// <summary>Command lines: split into words and joined again, quotes kept where a word needs them.</summary>
public static class RunCommandLine
{
    /// <summary>
    /// The words of <paramref name="line"/>: separated by spaces or tabs, a double-quoted part keeps its spaces (a quote
    /// inside is written <c>\"</c>); an empty line has none.
    /// </summary>
    public static IReadOnlyList<string> Split(string? line)
    {
        var words = new List<string>();
        if (string.IsNullOrWhiteSpace(line))
        {
            return words;
        }

        var word = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length && line[i + 1] == '"')
            {
                word.Append('"');
                any = true;
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (!quoted && (c == ' ' || c == '\t'))
            {
                if (any)
                {
                    words.Add(word.ToString());
                    word.Clear();
                    any = false;
                }
            }
            else
            {
                word.Append(c);
                any = true;
            }
        }

        if (any)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    /// <summary>The words as one line: each quoted when it is empty or holds a space or a quote.</summary>
    public static string Join(IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        return string.Join(' ', words.Select(word => word.Length == 0 || word.AsSpan().IndexOfAny(" \t\"") >= 0
            ? "\"" + word.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : word));
    }

    /// <summary>"A=1|B=two" as variables (an entry without a name or '=' is passed over); the inverse of <see cref="FormatEnvironment"/>.</summary>
    public static IReadOnlyDictionary<string, string> ParseEnvironment(string? text)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text))
        {
            return variables;
        }

        foreach (var range in text.AsSpan().Split('|'))
        {
            var entry = text.AsSpan()[range].Trim();
            int equals = entry.IndexOf('=');
            if (equals > 0)
            {
                variables[entry[..equals].Trim().ToString()] = entry[(equals + 1)..].ToString();
            }
        }

        return variables;
    }

    /// <summary>Variables as "A=1|B=two", in name order.</summary>
    public static string FormatEnvironment(IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(variables);
        return string.Join('|', variables.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + "=" + pair.Value));
    }
}

/// <summary>
/// The problems in a run's output as MSBuild and the compilers it drives write them:
/// <c>FILE(LINE,COL): error CODE: message [project]</c> (also <c>FILE(LINE,COL,ENDLINE,ENDCOL)</c>, <c>FILE(LINE)</c>,
/// <c>warning</c> and <c>info</c>), and a tool's <c>FILE : error CODE: message</c> at line 1 (a tool's own line, such as
/// <c>CSC : error CS2001</c>, names no file and is not one).
/// </summary>
public static partial class RunProblems
{
    /// <summary>The problem <paramref name="line"/> reports, a relative file taken from <paramref name="folder"/>; false when it reports none.</summary>
    public static bool TryParse(ReadOnlySpan<char> line, string folder, [NotNullWhen(true)] out RunProblem? problem)
    {
        ArgumentNullException.ThrowIfNull(folder);
        problem = null;
        // The cheap test first: almost every line of a build is not a problem (no allocation for those).
        if (line.IndexOf(": error ", StringComparison.Ordinal) < 0 && line.IndexOf(": warning ", StringComparison.Ordinal) < 0 && line.IndexOf(": info ", StringComparison.Ordinal) < 0)
        {
            return false;
        }

        foreach (var match in Located().EnumerateMatches(line))
        {
            var text = line.Slice(match.Index, match.Length).ToString();
            var parts = Located().Match(text);
            string file = parts.Groups["file"].Value.Trim();
            if (file.Length == 0 || file.Contains("://", StringComparison.Ordinal))
            {
                return false;
            }

            if (!parts.Groups["line"].Success && file.IndexOfAny(['.', '/', '\\']) < 0)
            {
                return false;                                    // a tool's own line ("CSC : error CS2001: ..."): no file to show
            }

            int lineNumber = parts.Groups["line"].Success ? int.Parse(parts.Groups["line"].Value, CultureInfo.InvariantCulture) : 1;
            int column = parts.Groups["col"].Success ? int.Parse(parts.Groups["col"].Value, CultureInfo.InvariantCulture) : 1;
            var severity = parts.Groups["severity"].Value switch
            {
                "error" => DiagnosticSeverity.Error,
                "warning" => DiagnosticSeverity.Warning,
                _ => DiagnosticSeverity.Info,
            };
            string message = parts.Groups["message"].Value.Trim();
            string full;
            try
            {
                full = Path.IsPathFullyQualified(file) ? Path.GetFullPath(file)
                    : Path.IsPathFullyQualified(folder) ? Path.GetFullPath(file, folder)
                    : Path.Combine(folder, file);                 // a folder of another system's form: kept as written
            }
            catch (ArgumentException)
            {
                return false;
            }

            problem = new RunProblem(full, Math.Max(1, lineNumber), Math.Max(1, column), severity, parts.Groups["code"].Value, message);
            return true;
        }

        return false;
    }

    // FILE(LINE[,COL[,ENDLINE,ENDCOL]]): SEVERITY CODE: MESSAGE [PROJECT], or FILE : SEVERITY CODE: MESSAGE.
    [GeneratedRegex(@"^\s*(?<file>[^\s(][^(]*?)(?:\((?<line>\d+)(?:,(?<col>\d+))?(?:,\d+,\d+)?\))?\s*:\s*(?<severity>error|warning|info)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*?)(?:\s+\[[^\]]+\])?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex Located();
}

/// <summary>
/// "scripts": PowerShell (<c>.ps1</c>), command (<c>.cmd</c>, <c>.bat</c>) and shell (<c>.sh</c>) scripts at the workspace's
/// root and in its <c>scripts</c> folder. A <c>.ps1</c> runs in PowerShell 7 (<c>pwsh</c>) or Windows PowerShell, a command
/// script in <c>cmd.exe</c> (Windows), a shell script in <c>bash</c> or <c>sh</c>, each found on the PATH at the run; one
/// that has no program here says so instead of running.
/// </summary>
internal sealed class ScriptRunProvider : IRunProvider
{
    public string Name => RunProviders.Scripts;

    public string Title => "Scripts";

    public string Summary => "PowerShell, command and shell scripts at the root and in a scripts folder";

    public bool Watches(ReadOnlySpan<char> fileName) => IsScript(fileName);

    public IReadOnlyList<RunTarget> Find(RunWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var targets = new List<RunTarget>();
        foreach (var file in workspace.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int slash = file.LastIndexOf('/');
            var folder = slash < 0 ? "" : file[..slash];
            if ((folder.Length == 0 || folder.Equals("scripts", StringComparison.OrdinalIgnoreCase)) && IsScript(file))
            {
                targets.Add(new RunTarget(Name, file, file[(slash + 1)..], file, RunActions.Run) { Detail = Kind(file), ReadsInput = true });
            }
        }

        targets.Sort(static (a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
        return targets;
    }

    public RunCommand Command(RunTarget target, RunActions action, RunConfiguration? configuration, string root)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string script = Path.GetFullPath(target.File, root);
        string folder = configuration?.Folder is { Length: > 0 } chosen ? Path.GetFullPath(chosen, root) : Path.GetDirectoryName(script) ?? root;
        if (action != RunActions.Run)
        {
            return RunCommand.Unavailable($"A script only runs ({target.Title} has no {action.ToString().ToLowerInvariant()}).", folder);
        }

        var arguments = RunCommandLine.Split(configuration?.Arguments);
        var environment = configuration?.Environment ?? RunConfiguration.NoEnvironment;
        var extension = Path.GetExtension(script).ToLowerInvariant();
        List<string>? command = extension switch
        {
            ".ps1" => Program("pwsh") is { } pwsh ? [pwsh, "-NoLogo", "-NoProfile", "-File", script]
                : OperatingSystem.IsWindows() && Program("powershell") is { } windows ? [windows, "-NoLogo", "-NoProfile", "-File", script]
                : null,
            ".cmd" or ".bat" => OperatingSystem.IsWindows() ? [Program("cmd") ?? "cmd.exe", "/d", "/c", script] : null,
            ".sh" => Program("bash") is { } bash ? [bash, script] : !OperatingSystem.IsWindows() && Program("sh") is { } sh ? [sh, script] : null,
            _ => null,
        };
        if (command is null)
        {
            string why = extension switch
            {
                ".ps1" => "PowerShell is not installed here (no pwsh on the PATH): install PowerShell 7 to run it.",
                ".cmd" or ".bat" => "A command script runs in Windows' cmd.exe, which this system does not have.",
                ".sh" => "No shell is on the PATH to run it (bash, or sh): on Windows, Git for Windows brings bash.",
                _ => $"{target.Title} is not a script this provider runs.",
            };
            return RunCommand.Unavailable(why, folder);
        }

        command.AddRange(arguments);
        return new RunCommand(command, folder) { Environment = environment };
    }

    private static bool IsScript(ReadOnlySpan<char> fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase) || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase) || extension.Equals(".sh", StringComparison.OrdinalIgnoreCase);
    }

    private static string Kind(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".ps1" => "PowerShell script",
        ".sh" => "shell script",
        _ => "command script",
    };

    private static string? Program(string name) => TerminalProgram.FindOnPath(name, System.Environment.GetEnvironmentVariable("PATH"), Path.PathSeparator,
        OperatingSystem.IsWindows() ? System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD" : null);
}
