// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;

namespace Craft.Abstractions;

/// <summary>
/// A shell the terminal can start (B9): its id, the name a menu shows ("PowerShell 7"), the program and its arguments,
/// an icon, and optionally its own start folder and environment additions. Found on the machine
/// (<see cref="ITerminalProfileSource"/>), written by the user (<c>CRAFT_TERMINAL_PROFILES</c>), or made from
/// <c>CRAFT_TERMINAL_SHELL</c>.
/// </summary>
/// <param name="Id">Its id, stable across runs ("pwsh", "git-bash", "wsl-ubuntu").</param>
/// <param name="Name">The name menus and the settings show ("PowerShell 7").</param>
/// <param name="Shell">The program and its arguments.</param>
public sealed record TerminalProfile(string Id, string Name, TerminalShell Shell)
{
    /// <summary>The icon, SVG path data on a 24 by 24 box (<see cref="TerminalIcons"/>).</summary>
    public string Icon { get; init; } = TerminalIcons.Prompt;

    /// <summary>The short name a tab shows ("pwsh", "cmd", "bash"); empty: the program's file name.</summary>
    public string ShortName { get; init; } = "";

    /// <summary>The folder it starts in, over the terminal's rule (an explorer's "Open in terminal" still wins), or null.</summary>
    public string? Folder { get; init; }

    /// <summary>Variables it adds to the terminal's environment (null removes one), or null.</summary>
    public IReadOnlyDictionary<string, string?>? Environment { get; init; }

    /// <summary>Where it comes from: a source's name ("machine"), "user" (the settings) or "setting" (<c>CRAFT_TERMINAL_SHELL</c>).</summary>
    public string Source { get; init; } = "";

    /// <summary>The short name, else the program's file name without its extension.</summary>
    public string Short => ShortName.Length > 0 ? ShortName : TerminalProgram.FileName(Shell.Program);

    /// <summary>Whether <paramref name="nameOrId"/> names it: its id or its name, ignoring case.</summary>
    public bool Is(string? nameOrId) =>
        !string.IsNullOrWhiteSpace(nameOrId) && (Id.Equals(nameOrId.Trim(), StringComparison.OrdinalIgnoreCase) || Name.Equals(nameOrId.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>The terminal profiles' icons: SVG path data on a 24 by 24 box, drawn as strokes (as <see cref="RailIcons"/>).</summary>
public static class TerminalIcons
{
    /// <summary>A prompt (a chevron and a cursor): any shell.</summary>
    public const string Prompt = "M5 7l5 5-5 5M12 17h7";

    /// <summary>PowerShell: a chevron in a slanted frame.</summary>
    public const string PowerShell = "M4 19L8 5h13l-4 14zM9 9l4 3-4 3M13 15h3";

    /// <summary>The Command Prompt: a window with a chevron.</summary>
    public const string CommandPrompt = "M3 5h18v14H3zM6 10l3 2-3 2M11 15h5";

    /// <summary>Bash and Git Bash: a dollar prompt in a frame.</summary>
    public const string Bash = "M3 5h18v14H3zM14 9h-3a1.5 1.5 0 0 0 0 3h2a1.5 1.5 0 0 1 0 3h-3M12 8v1M12 15v1";

    /// <summary>Linux (each WSL distribution): a penguin's outline.</summary>
    public const string Linux = "M12 3a4 4 0 0 0-4 4v4l-3 6c2 2 12 2 14 0l-3-6V7a4 4 0 0 0-4-4zM10 8h.01M14 8h.01M11 10.5h2";

    /// <summary>Visual Studio's developer shells: the infinity-like mark.</summary>
    public const string DeveloperShell = "M17 3l4 2v14l-4 2-10-9zM3 9l4 3-4 3z";
}

/// <summary>
/// A source of terminal profiles (B9), registered in <see cref="TerminalProfiles"/>: the library's "machine" (the shells
/// on this machine's PATH and in their usual folders), the Windows app's own "machine" over it (the registry's WSL
/// distributions and Git for Windows), or a plug-in's (containers, remote shells). <see cref="Find"/> is called off the
/// UI thread, once a discovery; it reads the disk and must not block on the user.
/// </summary>
public interface ITerminalProfileSource : IKind
{
    /// <summary>The profiles found now, in the order a menu lists them.</summary>
    IReadOnlyList<TerminalProfile> Find(CancellationToken cancellationToken = default);
}

/// <summary>
/// The terminal profile sources, by name (ignoring case): the library's "machine" (<see cref="MachineTerminalProfiles"/>
/// over <see cref="TerminalMachine.Local"/>), and what an app or a plug-in registers. <see cref="DiscoverAsync"/> asks
/// every source on the thread pool, in order, and keeps what they found (<see cref="LastFound"/> names it for the settings
/// page's drop-down). An unknown name fails with "not registered".
/// </summary>
public static class TerminalProfiles
{
    /// <summary>The library's source: the shells of this machine.</summary>
    public const string Machine = "machine";

    private static readonly KindTable<ITerminalProfileSource> Table = BuiltIn();
    private static IReadOnlyList<string> s_lastFound = [];

    private static KindTable<ITerminalProfileSource> BuiltIn()
    {
        var table = new KindTable<ITerminalProfileSource>(nameof(TerminalProfiles), "Terminal profile source", "add it with TerminalProfiles.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a source is asked once per discovery, off the UI thread");
        table.RegisterDefault(new MachineTerminalProfiles(Machine, TerminalMachine.Local));
        return table;
    }

    /// <summary>Registers <paramref name="source"/> under its name (over the library's of that name, which stays behind it).</summary>
    public static void Register(ITerminalProfileSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Table.Register(source, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the registration of that name (the library's, if any, answers again).</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered sources, in order.</summary>
    public static IReadOnlyList<ITerminalProfileSource> All => Table.Kinds;

    /// <summary>The source of that name, or null.</summary>
    public static ITerminalProfileSource? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The source of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static ITerminalProfileSource Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>The names of the profiles the last discovery found (the settings page's choices before the terminal adds the user's).</summary>
    public static IReadOnlyList<string> LastFound => Volatile.Read(ref s_lastFound);

    /// <summary>
    /// Asks every registered source for its profiles on the thread pool (never the caller's thread), in order; the first
    /// profile of an id wins. A source that throws is passed over and told to <paramref name="failed"/> (its name and the
    /// error), so one broken source never hides the others.
    /// </summary>
    public static Task<IReadOnlyList<TerminalProfile>> DiscoverAsync(Action<string, Exception>? failed = null, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var found = new List<TerminalProfile>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in Table.Kinds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<TerminalProfile> profiles;
                try
                {
                    profiles = source.Find(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031 // A source that fails is said and passed over; the others still count.
                catch (Exception error)
#pragma warning restore CA1031
                {
                    failed?.Invoke(source.Name, error);
                    continue;
                }

                foreach (var profile in profiles)
                {
                    if (profile is not null && ids.Add(profile.Id))
                    {
                        found.Add(profile.Source.Length > 0 ? profile : profile with { Source = source.Name });
                    }
                }
            }

            Volatile.Write(ref s_lastFound, [.. found.Select(p => p.Name)]);
            return (IReadOnlyList<TerminalProfile>)found;
        }, cancellationToken);
}

/// <summary>
/// What profile discovery reads of a machine (B9): its variables, its files and folders, and what only the platform can
/// tell (the WSL distributions and Git for Windows' folder, from the Windows registry). <see cref="Local"/> reads this
/// process's environment and disk (no registry); the Windows app makes one with the registry's reads; a test makes a fake.
/// </summary>
public sealed class TerminalMachine
{
    /// <summary>This process's machine: its environment and disk; no WSL distributions or Git folder known.</summary>
    public static TerminalMachine Local { get; } = new()
    {
        IsWindows = OperatingSystem.IsWindows(),
        Variable = System.Environment.GetEnvironmentVariable,
        FileExists = File.Exists,
        Folders = static path =>
        {
            try
            {
                return Directory.Exists(path) ? Directory.GetDirectories(path) : [];
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        },
    };

    /// <summary>Whether it is Windows (its shells and their names).</summary>
    public bool IsWindows { get; init; }

    /// <summary>An environment variable's value, or null.</summary>
    public Func<string, string?> Variable { get; init; } = static _ => null;

    /// <summary>Whether a file exists.</summary>
    public Func<string, bool> FileExists { get; init; } = static _ => false;

    /// <summary>A folder's subfolders (full paths), none when it does not exist.</summary>
    public Func<string, IReadOnlyList<string>> Folders { get; init; } = static _ => [];

    /// <summary>The installed WSL distributions' names (Windows: the registry's Lxss keys).</summary>
    public Func<IReadOnlyList<string>> WslDistributions { get; init; } = static () => [];

    /// <summary>Git for Windows' folder (the registry's InstallPath), or null.</summary>
    public Func<string?> GitFolder { get; init; } = static () => null;

    /// <summary><paramref name="program"/> on this machine's PATH (with PATHEXT on Windows), or null.</summary>
    public string? OnPath(string program) =>
        TerminalProgram.FindOnPath(program, Variable("PATH"), IsWindows ? ';' : ':', IsWindows ? Variable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD" : null, FileExists);
}

/// <summary>Program names and paths, platform-neutral (both separators read on every platform).</summary>
public static class TerminalProgram
{
    /// <summary>The file name of <paramref name="program"/> without its extension ("pwsh" of "C:\…\pwsh.exe").</summary>
    public static string FileName(string program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var trimmed = program.Trim().Trim('"');
        int slash = trimmed.LastIndexOfAny(['\\', '/']);
        var name = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        int dot = name.LastIndexOf('.');
        return dot > 0 ? name[..dot] : name;
    }

    /// <summary>
    /// <paramref name="program"/>'s full path on <paramref name="path"/> (folders separated by <paramref name="separator"/>),
    /// trying each of <paramref name="extensions"/> (";"-separated, Windows' PATHEXT) when it has none; null when it is not
    /// there. A rooted program is returned when it exists.
    /// </summary>
    public static string? FindOnPath(string program, string? path, char separator, string? extensions = null, Func<string, bool>? exists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        exists ??= File.Exists;
        var suffixes = Path.HasExtension(program) || string.IsNullOrEmpty(extensions)
            ? [""]
            : extensions.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (Path.IsPathRooted(program))
        {
            foreach (var suffix in suffixes)
            {
                if (exists(program + suffix))
                {
                    return program + suffix;
                }
            }

            return null;
        }

        foreach (var folder in (path ?? "").Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var suffix in suffixes)
            {
                var candidate = Path.Combine(folder.Trim('"'), program + suffix);
                if (exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A profile's command line split into its program and its arguments: a quoted program ("C:\Program Files\…\bash.exe"
    /// --login) keeps its spaces, else the program ends at the first space.
    /// </summary>
    public static TerminalShell Split(string commandLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        var text = commandLine.Trim();
        if (text[0] == '"')
        {
            int close = text.IndexOf('"', 1);
            return close < 0 ? new TerminalShell(text[1..]) : new TerminalShell(text[1..close], text[(close + 1)..].Trim());
        }

        int space = text.IndexOf(' ');
        return space < 0 ? new TerminalShell(text) : new TerminalShell(text[..space], text[(space + 1)..].Trim());
    }

    /// <summary>An id made of <paramref name="text"/>: lower case letters and digits, other runs as one '-'.</summary>
    public static string Slug(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var slug = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(char.ToLowerInvariant(c));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        return slug.ToString().TrimEnd('-');
    }
}

/// <summary>
/// The library's terminal profile source (B9): the shells of a <see cref="TerminalMachine"/>, read from its files and
/// variables only (no process started). On Windows: PowerShell 7 (the PATH, else Program Files), Windows PowerShell, the
/// Command Prompt, Git Bash, each WSL distribution, and Visual Studio's Developer PowerShell and Developer Command Prompt
/// for each installed edition; elsewhere: the login shell ($SHELL), then bash, zsh, fish and sh where found. Platform
/// neutral (the machine says what it is), so the rules are tested on any machine with a fake one.
/// </summary>
public sealed class MachineTerminalProfiles(string name, TerminalMachine machine) : ITerminalProfileSource
{
    private readonly TerminalMachine _machine = machine ?? throw new ArgumentNullException(nameof(machine));

    /// <inheritdoc />
    public string Name { get; } = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A source needs a name.", nameof(name)) : name;

    /// <inheritdoc />
    public string Summary => _machine.IsWindows
        ? "PowerShell 7, Windows PowerShell, the Command Prompt, Git Bash, WSL and Visual Studio's developer shells found on this machine"
        : "the login shell, bash, zsh, fish and sh found on this machine";

    /// <inheritdoc />
    public IReadOnlyList<TerminalProfile> Find(CancellationToken cancellationToken = default) =>
        _machine.IsWindows ? FindWindows(cancellationToken) : FindUnix(cancellationToken);

    private List<TerminalProfile> FindWindows(CancellationToken cancellationToken)
    {
        var found = new List<TerminalProfile>();
        var m = _machine;
        string? programFiles = m.Variable("ProgramFiles");
        string? programFilesX86 = m.Variable("ProgramFiles(x86)");
        string? windows = m.Variable("SystemRoot") ?? m.Variable("windir");
        string? system32 = windows is null ? null : Path.Combine(windows, "System32");

        // PowerShell 7: on the PATH (where its installer puts it), else its folder in Program Files.
        string? pwsh = m.OnPath("pwsh.exe") ?? First(programFiles is null ? [] : [Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"), Path.Combine(programFiles, "PowerShell", "7-preview", "pwsh.exe")]);
        if (pwsh is not null)
        {
            found.Add(new TerminalProfile("pwsh", "PowerShell 7", new TerminalShell(pwsh, "-NoLogo")) { Icon = TerminalIcons.PowerShell, ShortName = "pwsh" });
        }

        string? powershell = system32 is null ? null : First([Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe")]);
        if (powershell is not null)
        {
            found.Add(new TerminalProfile("powershell", "Windows PowerShell", new TerminalShell(powershell, "-NoLogo")) { Icon = TerminalIcons.PowerShell, ShortName = "powershell" });
        }

        string? cmd = First([m.Variable("ComSpec"), system32 is null ? null : Path.Combine(system32, "cmd.exe")]);
        if (cmd is not null)
        {
            found.Add(new TerminalProfile("cmd", "Command Prompt", new TerminalShell(cmd)) { Icon = TerminalIcons.CommandPrompt, ShortName = "cmd" });
        }

        cancellationToken.ThrowIfCancellationRequested();
        string? localPrograms = m.Variable("LOCALAPPDATA") is { } local ? Path.Combine(local, "Programs") : null;
        string? gitBash = First([.. new[] { m.GitFolder(), programFiles is null ? null : Path.Combine(programFiles, "Git"), programFilesX86 is null ? null : Path.Combine(programFilesX86, "Git"),
            localPrograms is null ? null : Path.Combine(localPrograms, "Git") }.Select(folder => folder is null ? null : Path.Combine(folder, "bin", "bash.exe"))]);
        if (gitBash is not null)
        {
            found.Add(new TerminalProfile("git-bash", "Git Bash", new TerminalShell(gitBash, "--login -i")) { Icon = TerminalIcons.Bash, ShortName = "bash" });
        }

        string? wsl = system32 is null ? null : First([Path.Combine(system32, "wsl.exe")]);
        if (wsl is not null)
        {
            foreach (var distribution in m.WslDistributions())
            {
                if (string.IsNullOrWhiteSpace(distribution))
                {
                    continue;
                }

                var quoted = distribution.Contains(' ', StringComparison.Ordinal) ? $"\"{distribution}\"" : distribution;
                var shortName = TerminalProgram.Slug(distribution.Split(['-', ' ', '_'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? distribution);
                found.Add(new TerminalProfile("wsl-" + TerminalProgram.Slug(distribution), distribution + " (WSL)", new TerminalShell(wsl, "-d " + quoted))
                {
                    Icon = TerminalIcons.Linux,
                    ShortName = shortName.Length > 0 ? shortName : "wsl",
                });
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        foreach (var root in (ReadOnlySpan<string?>)[programFiles, programFilesX86])
        {
            if (root is null)
            {
                continue;
            }

            foreach (var year in m.Folders(Path.Combine(root, "Microsoft Visual Studio")).Order(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var edition in m.Folders(year).Order(StringComparer.OrdinalIgnoreCase))
                {
                    AddDeveloperShells(found, edition, LastSegment(year), LastSegment(edition), powershell ?? pwsh, cmd);
                }
            }
        }

        return found;
    }

    // A folder's own name, either separator read (the rules run alike on every platform, rule 70).
    private static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        return trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
    }

    private void AddDeveloperShells(List<TerminalProfile> found, string install, string year, string edition, string? powershell, string? cmd)
    {
        var tools = Path.Combine(install, "Common7", "Tools");
        string id = TerminalProgram.Slug($"vs {year} {edition}");
        string label = $"for VS {year} {edition}";
        var launch = Path.Combine(tools, "Launch-VsDevShell.ps1");
        if (powershell is not null && _machine.FileExists(launch))
        {
            found.Add(new TerminalProfile(id + "-pwsh", "Developer PowerShell " + label,
                new TerminalShell(powershell, $"-NoExit -NoLogo -ExecutionPolicy Bypass -Command \"& '{launch}' -SkipAutomaticLocation\""))
            {
                Icon = TerminalIcons.DeveloperShell,
                ShortName = "devps",
            });
        }

        var devCmd = Path.Combine(tools, "VsDevCmd.bat");
        if (cmd is not null && _machine.FileExists(devCmd))
        {
            found.Add(new TerminalProfile(id + "-cmd", "Developer Command Prompt " + label, new TerminalShell(cmd, $"/k \"{devCmd}\""))
            {
                Icon = TerminalIcons.DeveloperShell,
                ShortName = "devcmd",
            });
        }
    }

    private List<TerminalProfile> FindUnix(CancellationToken cancellationToken)
    {
        var found = new List<TerminalProfile>();
        var programs = new HashSet<string>(StringComparer.Ordinal);
        if (_machine.Variable("SHELL") is { Length: > 0 } login && _machine.FileExists(login))
        {
            Add(found, programs, login);
        }

        foreach (var shell in (ReadOnlySpan<string>)["bash", "zsh", "fish", "pwsh", "sh"])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((_machine.OnPath(shell) ?? First(["/bin/" + shell, "/usr/bin/" + shell])) is { } path)
            {
                Add(found, programs, path);
            }
        }

        return found;

        static void Add(List<TerminalProfile> found, HashSet<string> programs, string path)
        {
            var name = TerminalProgram.FileName(path);
            if (!programs.Add(name))
            {
                return;                                   // the login shell is listed once, under its own name
            }

            found.Add(new TerminalProfile(name, name, new TerminalShell(path, name is "bash" or "zsh" ? "-l" : ""))
            {
                Icon = name == "pwsh" ? TerminalIcons.PowerShell : TerminalIcons.Bash,
                ShortName = name,
            });
        }
    }

    private string? First(IEnumerable<string?> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && _machine.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The profiles' ids and names as one line (the tests' and the log's view).</summary>
    public static string Describe(IEnumerable<TerminalProfile> profiles) =>
        string.Join(", ", (profiles ?? throw new ArgumentNullException(nameof(profiles))).Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Id} ({p.Name})")));
}
