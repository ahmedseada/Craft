// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text.Json;
using System.Xml;
using Craft.Abstractions;

namespace Craft.CSharp.Run;

/// <summary>
/// "dotnet" (X part 6), the C# plug-in's run provider: every <c>.csproj</c> by its <c>OutputType</c> (an Exe, WinExe, a web
/// or worker SDK, or a MAUI app runs and publishes; a test project, by <c>IsTestProject</c> or a test framework's package,
/// tests; every project builds), every <c>.sln</c> and <c>.slnx</c> (build, test), and each project's
/// <c>Properties/launchSettings.json</c> profiles ("Project": <c>dotnet run --launch-profile</c>; "Executable": the
/// program itself), with their arguments, variables and address. The commands are the .NET SDK's own
/// (<c>dotnet run/build/test/publish</c>), found on the PATH when they run; a multi-targeted project runs its framework
/// for this system (a <c>-windows</c> one on Windows, else one without a system).
/// </summary>
public sealed class DotnetRunProvider : IRunProvider
{
    /// <summary>Its name in <see cref="RunProviders"/>.</summary>
    public const string ProviderName = "dotnet";

    private static readonly string[] TestPackages = ["Microsoft.NET.Test.Sdk", "xunit", "xunit.v3", "NUnit", "MSTest", "MSTest.TestFramework", "TUnit"];

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Title => "C# (.NET)";

    /// <inheritdoc />
    public string Summary => ".csproj by OutputType (run, build, test, publish), .sln and .slnx (build, test), launchSettings.json profiles; dotnet run/build/test/publish";

    /// <inheritdoc />
    public bool Watches(ReadOnlySpan<char> fileName)
    {
        var extension = Path.GetExtension(fileName);
        return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) || extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) || fileName.Equals("launchSettings.json", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public IReadOnlyList<RunTarget> Find(RunWorkspace workspace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var solutions = new List<RunTarget>();
        var projects = new List<RunTarget>();
        foreach (var file in workspace.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var extension = Path.GetExtension(file.AsSpan());
            if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                solutions.Add(new RunTarget(Name, file, Path.GetFileName(file), file, RunActions.Build | RunActions.Test) { Detail = "solution" });
            }
            else if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                var project = ReadProject(Path.Combine(workspace.Root, file));
                string title = Path.GetFileNameWithoutExtension(file);
                var actions = RunActions.Build | (project.Runs ? RunActions.Run | RunActions.Publish : RunActions.None) | (project.Tests ? RunActions.Test : RunActions.None);
                string kind = project.Tests ? "Tests" : project.Runs ? project.OutputType ?? "Exe" : project.OutputType ?? "Library";
                string frameworks = project.Frameworks.Count == 0 ? "" : " · " + string.Join(", ", project.Frameworks);
                projects.Add(new RunTarget(Name, file, title, file, actions) { Detail = kind + frameworks });
                if (project.Runs)
                {
                    projects.AddRange(Profiles(workspace.Root, file, title));
                }
            }
        }

        solutions.Sort(static (a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
        projects.Sort(static (a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
        // A project that runs first, so the first target (F5 with no default) is one that runs.
        return [.. projects.Where(p => (p.Actions & RunActions.Run) != 0), .. solutions, .. projects.Where(p => (p.Actions & RunActions.Run) == 0)];
    }

    /// <inheritdoc />
    public RunCommand Command(RunTarget target, RunActions action, RunConfiguration? configuration, string root)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string file = Path.GetFullPath(target.File, root);
        string folder = configuration?.Folder is { Length: > 0 } chosen ? Path.GetFullPath(chosen, root) : Path.GetDirectoryName(file) ?? root;
        if ((target.Actions & action) == 0)
        {
            return RunCommand.Unavailable($"{target.Title} cannot {action.ToString().ToLowerInvariant()}.", folder);
        }

        var extra = RunCommandLine.Split(configuration?.Arguments);
        var environment = Merge(target.Environment, configuration?.Environment);
        int hash = target.Id.IndexOf('#', StringComparison.Ordinal);
        string? profile = hash >= 0 ? target.Id[(hash + 1)..] : null;
        if (target.Program is { } program)
        {
            // An "Executable" profile: its program, its arguments, its folder.
            return new RunCommand([program, .. RunCommandLine.Split(target.Arguments), .. extra], folder) { Environment = environment };
        }

        if (Dotnet() is not { } dotnet)
        {
            return RunCommand.Unavailable("The .NET SDK is not installed here (no dotnet on the PATH): install it from dot.net to run and build C#.", folder);
        }

        var command = new List<string> { dotnet };
        switch (action)
        {
            case RunActions.Run:
                command.Add("run");
                command.Add("--project");
                command.Add(file);
                var frameworks = ReadProject(file).Frameworks;
                if (frameworks.Count > 1)
                {
                    command.Add("--framework");
                    command.Add(FrameworkFor(frameworks));
                }

                if (profile is not null)
                {
                    command.Add("--launch-profile");
                    command.Add(profile);
                }

                var arguments = target.Arguments is { Length: > 0 } given ? RunCommandLine.Split(given) : [];
                if (arguments.Count + extra.Count > 0)
                {
                    command.Add("--");
                    command.AddRange(arguments);
                    command.AddRange(extra);
                }

                break;
            default:
                command.Add(action switch
                {
                    RunActions.Build => "build",
                    RunActions.Test => "test",
                    _ => "publish",
                });
                command.Add(file);
                command.Add("-nologo");
                command.AddRange(extra);
                break;
        }

        return new RunCommand(command, folder) { Environment = environment };
    }

    /// <summary>The framework a multi-targeted project runs here: one for this system (net10.0-windows… on Windows), else one for none, else the first.</summary>
    public static string FrameworkFor(IReadOnlyList<string> frameworks)
    {
        ArgumentNullException.ThrowIfNull(frameworks);
        string system = OperatingSystem.IsWindows() ? "-windows" : OperatingSystem.IsMacOS() ? "-macos" : OperatingSystem.IsAndroid() ? "-android" : "-linux";
        return frameworks.FirstOrDefault(f => f.Contains(system, StringComparison.OrdinalIgnoreCase))
            ?? frameworks.FirstOrDefault(f => !f.Contains('-', StringComparison.Ordinal))
            ?? frameworks[0];
    }

    /// <summary>What a project file says about running it: its output type, its frameworks, whether it runs and whether it tests.</summary>
    /// <param name="OutputType">Its <c>OutputType</c> as written, or null.</param>
    /// <param name="Frameworks">Its target frameworks.</param>
    /// <param name="Runs">Whether it is a program (Exe, WinExe, a web or worker SDK, a MAUI app).</param>
    /// <param name="Tests">Whether it is a test project.</param>
    public sealed record ProjectFacts(string? OutputType, IReadOnlyList<string> Frameworks, bool Runs, bool Tests);

    /// <summary>Reads <paramref name="path"/> (a .csproj) as an XML stream: its SDK, output type, frameworks and packages; a file that does not read is a library.</summary>
    public static ProjectFacts ReadProject(string path)
    {
        string? outputType = null, sdk = null;
        var frameworks = new List<string>();
        bool testProject = false, maui = false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true, IgnoreWhitespace = true });
            // Reading an element's text moves the reader past it, onto the next node: that node is looked at next, not skipped.
            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    reader.Read();
                    continue;
                }

                bool consumed = reader.LocalName is "OutputType" or "TargetFramework" or "TargetFrameworks" or "IsTestProject" or "UseMaui";
                switch (reader.LocalName)
                {
                    case "Project":
                        sdk = reader.GetAttribute("Sdk");
                        break;
                    case "OutputType":
                        outputType = reader.ReadElementContentAsString().Trim();
                        break;
                    case "TargetFramework":
                        frameworks.Add(reader.ReadElementContentAsString().Trim());
                        break;
                    case "TargetFrameworks":
                        frameworks.AddRange(reader.ReadElementContentAsString().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(f => !f.Contains('$', StringComparison.Ordinal)));
                        break;
                    case "IsTestProject":
                        testProject |= reader.ReadElementContentAsString().Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "UseMaui":
                        maui |= reader.ReadElementContentAsString().Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "PackageReference":
                        if (reader.GetAttribute("Include") is { } package && TestPackages.Any(p => p.Equals(package, StringComparison.OrdinalIgnoreCase)))
                        {
                            testProject = true;
                        }

                        break;
                }

                if (!consumed)
                {
                    reader.Read();
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or XmlException)
        {
            return new ProjectFacts(null, [], false, false);
        }

        bool programSdk = sdk is not null && (sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase) || sdk.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase));
        bool exe = outputType is not null
            ? outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) || outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase)
            : programSdk || maui;
        return new ProjectFacts(outputType, frameworks.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), exe && !testProject, testProject);
    }

    // The launch profiles of a project that runs: "Project" ones through dotnet run, "Executable" ones as their program.
    private IEnumerable<RunTarget> Profiles(string root, string project, string title)
    {
        string folder = Path.GetDirectoryName(project.Replace('/', Path.DirectorySeparatorChar)) ?? "";
        string file = Path.Combine(root, folder, "Properties", "launchSettings.json");
        if (!File.Exists(file))
        {
            yield break;
        }

        List<RunTarget> profiles = [];
        try
        {
            var bytes = File.ReadAllBytes(file);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            using var document = JsonDocument.ParseValue(ref reader);
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("profiles", out var all) && all.ValueKind == JsonValueKind.Object)
            {
                foreach (var profile in all.EnumerateObject())
                {
                    if (Profile(profile, project, title, root, folder) is { } target)
                    {
                        profiles.Add(target);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            yield break;
        }

        foreach (var profile in profiles)
        {
            yield return profile;
        }
    }

    private RunTarget? Profile(JsonProperty profile, string project, string title, string root, string folder)
    {
        var value = profile.Value;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string? Text(string name) => value.TryGetProperty(name, out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() : null;
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (value.TryGetProperty("environmentVariables", out var variables) && variables.ValueKind == JsonValueKind.Object)
        {
            foreach (var variable in variables.EnumerateObject())
            {
                environment[variable.Name] = variable.Value.ValueKind == JsonValueKind.String ? variable.Value.GetString() ?? "" : variable.Value.GetRawText();
            }
        }

        string? url = Text("applicationUrl")?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        string id = project + "#" + profile.Name;
        string detail = "launch profile" + (url is null ? "" : " · " + url);
        switch (Text("commandName"))
        {
            case "Project":
                return new RunTarget(Name, id, $"{title} · {profile.Name}", project, RunActions.Run)
                {
                    Detail = detail,
                    Arguments = Text("commandLineArgs"),
                    Environment = environment,
                    Url = url,
                };
            case "Executable" when Text("executablePath") is { Length: > 0 } executable:
                string program = Path.GetFullPath(Environment.ExpandEnvironmentVariables(executable), Path.Combine(root, folder));
                return new RunTarget(Name, id, $"{title} · {profile.Name}", project, RunActions.Run)
                {
                    Detail = "launch profile · " + Path.GetFileName(program),
                    Arguments = Text("commandLineArgs"),
                    Environment = environment,
                    Url = url,
                    Program = program,
                };
            default:
                return null;                                         // IIS Express, Docker and the like: not run from here
        }
    }

    private static IReadOnlyDictionary<string, string> Merge(IReadOnlyDictionary<string, string> first, IReadOnlyDictionary<string, string>? second)
    {
        if (second is null || second.Count == 0)
        {
            return first;
        }

        var merged = new Dictionary<string, string>(first, StringComparer.Ordinal);
        foreach (var (name, value) in second)
        {
            merged[name] = value;
        }

        return merged;
    }

    private static string? Dotnet() => TerminalProgram.FindOnPath("dotnet", Environment.GetEnvironmentVariable("PATH"), Path.PathSeparator,
        OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD" : null);
}
