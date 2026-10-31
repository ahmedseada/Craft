// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text.Json;
using Craft.Abstractions;

namespace Craft.CSharp.Workspaces;

/// <summary>
/// "csproj": a C# project file read without MSBuild (<see cref="ProjectEvaluator"/>): its target framework, language
/// version, nullable context, implicit usings (the SDK's set for Microsoft.NET.Sdk and Microsoft.NET.Sdk.Web, plus the
/// <c>Using</c> items), defines (the SDK's for the configuration and framework, plus <c>DefineConstants</c>),
/// <c>Compile</c> items (the default <c>**/*.cs</c> without bin and obj, then <c>Include</c> and <c>Remove</c>), and
/// references: from <c>obj/project.assets.json</c> when a restore left one (each package's compile assets for the
/// framework, from the packages folder it names, and the projects it lists), else the framework's alone (said so in the
/// project's notes); <c>ProjectReference</c> items are read as projects too. For a file, the project is the nearest
/// <c>.csproj</c> above it (up to the workspace root); without a file, every <c>.csproj</c> under the root.
/// </summary>
public sealed class CsprojWorkspaceReader : IWorkspaceReader
{
    /// <summary>Its name in <see cref="WorkspaceReaders"/>.</summary>
    public const string ReaderName = "csproj";

    private static readonly string[] SdkUsings = ["System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks"];

    private static readonly string[] WebUsings = ["System.Net.Http.Json", "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Routing", "Microsoft.Extensions.Configuration", "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Hosting", "Microsoft.Extensions.Logging"];

    /// <inheritdoc />
    public string Name => ReaderName;

    /// <inheritdoc />
    public string Summary => "C# project files (.csproj), read without MSBuild; references from obj/project.assets.json";

    /// <inheritdoc />
    public WorkspaceModel? Read(WorkspaceReadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var roots = new List<string>();
        if (request.FilePath is { } file)
        {
            if (FindProject(Path.GetDirectoryName(Path.GetFullPath(file))!, request.Root) is { } project)
            {
                roots.Add(project);
            }
        }
        else if (request.Root is { } root && Directory.Exists(root))
        {
            foreach (var project in Directory.EnumerateFiles(root, "*.csproj", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                if (!IsIgnoredPath(Path.GetRelativePath(root, project)))
                {
                    roots.Add(project);
                }
            }

            roots.Sort(StringComparer.OrdinalIgnoreCase);
        }

        if (roots.Count == 0)
        {
            return null;
        }

        var projects = new List<WorkspaceProject>();
        var seen = new HashSet<string>(PathComparer);
        foreach (var project in roots)
        {
            Load(project, request.Settings, projects, seen, cancellationToken);
        }

        return new WorkspaceModel(ReaderName, request.Root ?? Path.GetDirectoryName(roots[0])!, projects);
    }

    /// <summary>The nearest project file at or above <paramref name="folder"/>, not above <paramref name="root"/> (when given).</summary>
    public static string? FindProject(string folder, string? root)
    {
        var stop = root is null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        for (var at = new DirectoryInfo(folder); at is not null; at = at.Parent)
        {
            if (Directory.Exists(at.FullName))
            {
                var found = Directory.GetFiles(at.FullName, "*.csproj");
                if (found.Length > 0)
                {
                    Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                    return found[0];
                }
            }

            if (stop is not null && PathComparer.Equals(Path.TrimEndingDirectorySeparator(at.FullName), stop))
            {
                break;
            }
        }

        return null;
    }

    internal static StringComparer PathComparer { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsIgnoredPath(string relative)
    {
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.Equals("bin", StringComparison.OrdinalIgnoreCase) || part.Equals("obj", StringComparison.OrdinalIgnoreCase) || (part.StartsWith('.') && part.Length > 1 && part != ".."))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads <paramref name="projectFile"/> and the projects it references (those first) into <paramref name="into"/>.</summary>
    private static void Load(string projectFile, LanguageSettings settings, List<WorkspaceProject> into, HashSet<string> seen, CancellationToken cancellationToken)
    {
        projectFile = Path.GetFullPath(projectFile);
        if (!seen.Add(projectFile) || !File.Exists(projectFile))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var project = Read(projectFile, settings);
        foreach (var reference in project.ProjectReferences)
        {
            Load(reference, settings, into, seen, cancellationToken);
        }

        into.Add(project);
    }

    /// <summary>One project file as a <see cref="WorkspaceProject"/>.</summary>
    public static WorkspaceProject Read(string projectFile, LanguageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var evaluator = new ProjectEvaluator(projectFile, settings.Configuration);
        string? tfm = evaluator["TargetFramework"];
        if (tfm is null && evaluator["TargetFrameworks"] is { } many)
        {
            tfm = many.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (tfm is not null)
            {
                evaluator = new ProjectEvaluator(projectFile, settings.Configuration, tfm);
            }
        }

        var notes = new List<string>(evaluator.Notes.Distinct(StringComparer.Ordinal));
        var folder = evaluator.Folder;
        var name = evaluator["AssemblyName"] ?? Path.GetFileNameWithoutExtension(projectFile);

        // Usings: the SDK's implicit ones, then the Using items (Include adds, Remove takes away).
        var usings = new List<string>();
        var implicitUsings = evaluator["ImplicitUsings"];
        if (implicitUsings is not null && (implicitUsings.Equals("enable", StringComparison.OrdinalIgnoreCase) || implicitUsings.Equals("true", StringComparison.OrdinalIgnoreCase)))
        {
            usings.AddRange(SdkUsings);
            if (evaluator.Sdk is { } sdk && sdk.StartsWith("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
            {
                usings.AddRange(WebUsings);
            }
        }

        foreach (var (include, remove, element, _) in evaluator.Items("Using"))
        {
            if (remove is not null)
            {
                usings.RemoveAll(u => u.Equals(remove, StringComparison.Ordinal));
                continue;
            }

            foreach (var part in Split(include))
            {
                var alias = evaluator.Metadata(element, "Alias");
                bool isStatic = evaluator.Metadata(element, "Static") is { } s && s.Equals("true", StringComparison.OrdinalIgnoreCase);
                var entry = alias is { Length: > 0 } ? $"{alias} = {part}" : isStatic ? "static " + part : part;
                if (!usings.Contains(entry))
                {
                    usings.Add(entry);
                }
            }
        }

        var internals = new List<string>();
        foreach (var (include, _, _, _) in evaluator.Items("InternalsVisibleTo"))
        {
            internals.AddRange(Split(include));
        }

        var defines = Defines(evaluator, settings.Configuration, tfm);
        var sources = Sources(evaluator);
        var references = new List<string>();
        var projectReferences = new List<string>();
        foreach (var (include, _, _, itemFolder) in evaluator.Items("ProjectReference"))
        {
            foreach (var part in Split(include))
            {
                projectReferences.Add(Path.GetFullPath(Path.Combine(itemFolder, Normalize(part))));
            }
        }

        foreach (var (include, _, element, itemFolder) in evaluator.Items("Reference"))
        {
            if (evaluator.Metadata(element, "HintPath") is { Length: > 0 } hint)
            {
                references.Add(Path.GetFullPath(Path.Combine(itemFolder, Normalize(hint))));
            }
            else if (include.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                references.Add(Path.GetFullPath(Path.Combine(itemFolder, Normalize(include))));
            }
        }

        var frameworks = new List<string>();
        var analyzers = new List<string>();
        foreach (var (include, _, _, itemFolder) in evaluator.Items("Analyzer"))
        {
            foreach (var part in Split(include))
            {
                analyzers.Add(Path.GetFullPath(Path.Combine(itemFolder, Normalize(part))));
            }
        }

        var assets = Path.Combine(evaluator["MSBuildProjectExtensionsPath"] is { } extensions ? Path.Combine(folder, Normalize(extensions)) : Path.Combine(folder, "obj"), "project.assets.json");
        if (File.Exists(assets))
        {
            ReadAssets(assets, folder, tfm, references, projectReferences, frameworks, analyzers, notes);
        }
        else
        {
            notes.Add("no obj/project.assets.json (no restore yet): packages are not referenced, only the framework");
        }

        foreach (var (include, _, _, _) in evaluator.Items("FrameworkReference"))
        {
            frameworks.AddRange(Split(include));
        }

        var framework = ReferenceAssemblies.For(tfm, frameworks);
        notes.AddRange(framework.Notes);
        var allAnalyzers = new List<string>(framework.Analyzers);
        var knownAnalyzers = new HashSet<string>(framework.Analyzers.Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        foreach (var analyzer in analyzers)
        {
            // A package that carries an analyzer the framework's pack has gives way to the pack's (the SDK does the same).
            if (knownAnalyzers.Add(Path.GetFileName(analyzer)))
            {
                allAnalyzers.Add(analyzer);
            }
        }

        var allReferences = new List<string>(framework.Files);
        var known = new HashSet<string>(framework.Files.Select(Path.GetFileNameWithoutExtension)!, StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references)
        {
            // A package that carries an assembly the framework has (an older System.* package) gives way to the framework's.
            if (known.Add(Path.GetFileNameWithoutExtension(reference)))
            {
                allReferences.Add(reference);
            }
        }

        var noWarn = new List<string> { "CS1701", "CS1702" };
        if (evaluator["NoWarn"] is { } suppressed)
        {
            foreach (var id in suppressed.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                noWarn.Add(id.Length > 0 && char.IsAsciiDigit(id[0]) ? "CS" + id : id);
            }
        }

        int? warningLevel = evaluator["WarningLevel"] is { } level && int.TryParse(level, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed
            : MajorVersion(tfm) is >= 5 and var major ? major : null;
        var outputType = evaluator["OutputType"];
        return new WorkspaceProject
        {
            Name = name,
            ProjectFile = evaluator.ProjectFile,
            Folder = folder,
            TargetFramework = tfm,
            LanguageVersion = evaluator["LangVersion"],
            Nullable = evaluator["Nullable"],
            AllowUnsafe = evaluator.Flag("AllowUnsafeBlocks"),
            TreatWarningsAsErrors = evaluator.Flag("TreatWarningsAsErrors"),
            DocumentationDiagnostics = evaluator.Flag("GenerateDocumentationFile") || evaluator["DocumentationFile"] is not null,
            WarningLevel = warningLevel,
            NoWarn = noWarn,
            IsExecutable = outputType is not null && (outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) || outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase)),
            DefineConstants = defines,
            GlobalUsings = usings,
            InternalsVisibleTo = internals,
            SourceFiles = sources,
            References = allReferences,
            Analyzers = allAnalyzers,
            ProjectReferences = projectReferences.Distinct(PathComparer).Where(p => !PathComparer.Equals(p, evaluator.ProjectFile)).ToList(),
            Notes = notes,
        };
    }

    private static string[] Split(string items) => items.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Normalize(string path) => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static int? MajorVersion(string? tfm)
    {
        if (tfm is null || !tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase) || tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) || tfm.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var version = ReferenceAssemblies.BaseFramework(tfm)[3..];
        int dot = version.IndexOf('.', StringComparison.Ordinal);
        return dot > 0 && int.TryParse(version.AsSpan(0, dot), NumberStyles.Integer, CultureInfo.InvariantCulture, out var major) ? major : null;
    }

    /// <summary>The defines the SDK makes for the configuration and the framework, then the project's own.</summary>
    private static List<string> Defines(ProjectEvaluator evaluator, string configuration, string? tfm)
    {
        var defines = new List<string>();
        void Add(string symbol)
        {
            if (symbol.Length > 0 && !defines.Contains(symbol))
            {
                defines.Add(symbol);
            }
        }

        if (evaluator["DefineConstants"] is { } own)
        {
            foreach (var symbol in own.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Add(symbol);
            }
        }

        Add("TRACE");
        Add(configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase) ? "DEBUG" : configuration.Equals("Release", StringComparison.OrdinalIgnoreCase) ? "RELEASE" : "");
        if (tfm is null)
        {
            return defines;
        }

        var baseTfm = ReferenceAssemblies.BaseFramework(tfm);
        if (MajorVersion(tfm) is { } major)
        {
            int dot = baseTfm.IndexOf('.', StringComparison.Ordinal);
            var minor = baseTfm[(dot + 1)..];
            Add("NET");
            Add("NETCOREAPP");
            Add(string.Create(CultureInfo.InvariantCulture, $"NET{major}_{minor}"));
            for (int v = 5; v <= major; v++)
            {
                Add(string.Create(CultureInfo.InvariantCulture, $"NET{v}_0_OR_GREATER"));
            }

            foreach (var core in (ReadOnlySpan<string>)["NETCOREAPP1_0", "NETCOREAPP1_1", "NETCOREAPP2_0", "NETCOREAPP2_1", "NETCOREAPP2_2", "NETCOREAPP3_0", "NETCOREAPP3_1"])
            {
                Add(core + "_OR_GREATER");
            }
        }
        else if (baseTfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
        {
            Add("NETSTANDARD");
            Add(baseTfm.ToUpperInvariant().Replace('.', '_'));
        }

        if (ProjectEvaluator.PlatformOf(tfm) is { Length: > 0 } platform)
        {
            Add(platform.ToUpperInvariant());
        }

        return defines;
    }

    /// <summary>The Compile items: the default glob (unless turned off) without bin, obj and hidden folders, then Include and Remove in order.</summary>
    private static List<string> Sources(ProjectEvaluator evaluator)
    {
        var folder = evaluator.Folder;
        var files = new List<string>();
        var set = new HashSet<string>(PathComparer);
        bool defaults = !(evaluator["EnableDefaultItems"] is { } items && items.Equals("false", StringComparison.OrdinalIgnoreCase))
            && !(evaluator["EnableDefaultCompileItems"] is { } compile && compile.Equals("false", StringComparison.OrdinalIgnoreCase));
        if (defaults)
        {
            foreach (var file in Glob.Expand(folder, "**/*.cs"))
            {
                if (!IsIgnoredPath(Path.GetRelativePath(folder, file)) && set.Add(file))
                {
                    files.Add(file);
                }
            }
        }

        foreach (var (include, remove, _, itemFolder) in evaluator.Items("Compile"))
        {
            if (remove is not null)
            {
                foreach (var pattern in Split(remove))
                {
                    var matcher = Glob.Matcher(itemFolder, pattern);
                    files.RemoveAll(f => matcher(f) && set.Remove(f));
                }
            }

            foreach (var pattern in Split(include))
            {
                foreach (var file in Glob.Expand(itemFolder, pattern))
                {
                    if (set.Add(file))
                    {
                        files.Add(file);
                    }
                }
            }
        }

        return files;
    }

    /// <summary>
    /// A package's C# analyzers, as NuGet picks them from its files: <c>analyzers/dotnet[/roslynX.Y][/cs]/*.dll</c> (not
    /// another language's folder, not a satellite resource), the highest <c>roslynX.Y</c> folder the running Roslyn can load
    /// when the package has several.
    /// </summary>
    private static void PackageAnalyzers(JsonElement files, string packageFolder, List<string> analyzers)
    {
        var candidates = new List<(Version? Roslyn, string File)>();
        var roslyn = typeof(Microsoft.CodeAnalysis.Compilation).Assembly.GetName().Version ?? new Version(0, 0);
        foreach (var file in files.EnumerateArray())
        {
            if (file.GetString() is not { } name || !name.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = name.Split('/');
            Version? version = null;
            bool other = false;
            for (int i = 1; i < parts.Length - 1; i++)
            {
                var part = parts[i];
                if (part.StartsWith("roslyn", StringComparison.OrdinalIgnoreCase) && Version.TryParse(part.AsSpan(6), out var v))
                {
                    version = v;
                }
                else if (part.Equals("vb", StringComparison.OrdinalIgnoreCase) || part.Equals("fs", StringComparison.OrdinalIgnoreCase))
                {
                    other = true;
                }
            }

            if (!other && (version is null || version <= roslyn))
            {
                candidates.Add((version, name));
            }
        }

        var best = candidates.Where(c => c.Roslyn is not null).Select(c => c.Roslyn!).DefaultIfEmpty().Max();
        foreach (var (version, name) in candidates)
        {
            if (best is not null && version != best)
            {
                continue;
            }

            var full = Path.GetFullPath(Path.Combine(packageFolder, Normalize(name)));
            if (File.Exists(full))
            {
                analyzers.Add(full);
            }
        }
    }

    /// <summary>Reads a restore's assets: the compile assets of each package for the framework, its projects and framework references.</summary>
    private static void ReadAssets(string path, string projectFolder, string? tfm, List<string> references, List<string> projectReferences, List<string> frameworks,
        List<string> analyzers, List<string> notes)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            string? packages = null;
            if (root.TryGetProperty("packageFolders", out var folders))
            {
                foreach (var folder in folders.EnumerateObject())
                {
                    packages = folder.Name;
                    break;
                }
            }

            if (!root.TryGetProperty("targets", out var targets) || !root.TryGetProperty("libraries", out var libraries))
            {
                notes.Add("obj/project.assets.json has no targets: packages are not referenced");
                return;
            }

            JsonElement? target = null;
            foreach (var candidate in targets.EnumerateObject())
            {
                if (tfm is null || candidate.Name.Equals(tfm, StringComparison.OrdinalIgnoreCase) || candidate.Name.StartsWith(tfm + "/", StringComparison.OrdinalIgnoreCase))
                {
                    target = candidate.Value;
                    break;
                }

                target ??= candidate.Value;
            }

            if (target is not { } entries)
            {
                return;
            }

            foreach (var entry in entries.EnumerateObject())
            {
                string type = entry.Value.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                if (!libraries.TryGetProperty(entry.Name, out var library) || !library.TryGetProperty("path", out var libraryPath) || libraryPath.GetString() is not { } relative)
                {
                    continue;
                }

                if (type == "project")
                {
                    var projectPath = library.TryGetProperty("msbuildProject", out var msbuild) && msbuild.GetString() is { } m ? m : relative;
                    projectReferences.Add(Path.GetFullPath(Path.Combine(projectFolder, Normalize(projectPath))));
                    continue;
                }

                if (packages is not null && type == "package" && library.TryGetProperty("files", out var files))
                {
                    PackageAnalyzers(files, Path.Combine(packages, Normalize(relative)), analyzers);
                }

                if (packages is null || !entry.Value.TryGetProperty("compile", out var compile))
                {
                    continue;
                }

                foreach (var asset in compile.EnumerateObject())
                {
                    if (asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        var file = Path.GetFullPath(Path.Combine(packages, Normalize(relative), Normalize(asset.Name)));
                        if (File.Exists(file))
                        {
                            references.Add(file);
                        }
                    }
                }
            }

            if (root.TryGetProperty("project", out var project) && project.TryGetProperty("frameworks", out var projectFrameworks))
            {
                foreach (var framework in projectFrameworks.EnumerateObject())
                {
                    if ((tfm is null || framework.Name.Equals(tfm, StringComparison.OrdinalIgnoreCase)) && framework.Value.TryGetProperty("frameworkReferences", out var named))
                    {
                        foreach (var reference in named.EnumerateObject())
                        {
                            frameworks.Add(reference.Name);
                        }
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            notes.Add($"obj/project.assets.json could not be read ({e.Message}): packages are not referenced");
        }
    }
}

/// <summary>MSBuild-style globs over files: <c>*</c>, <c>?</c> and <c>**</c> (any folders), relative to a folder.</summary>
internal static class Glob
{
    /// <summary>The files <paramref name="pattern"/> (relative to <paramref name="folder"/>, or rooted) names; a pattern without wildcards names one file.</summary>
    public static IEnumerable<string> Expand(string folder, string pattern)
    {
        var full = Path.GetFullPath(Path.Combine(folder, Normalize(pattern)));
        int wild = full.IndexOfAny(['*', '?']);
        if (wild < 0)
        {
            if (File.Exists(full))
            {
                yield return full;
            }

            yield break;
        }

        int cut = full.LastIndexOf(Path.DirectorySeparatorChar, wild);
        var baseFolder = full[..Math.Max(cut, 0)];
        if (baseFolder.Length == 0 || !Directory.Exists(baseFolder))
        {
            yield break;
        }

        var matcher = Matcher(folder, pattern);
        var options = new EnumerationOptions { RecurseSubdirectories = full.AsSpan(cut).Contains("**", StringComparison.Ordinal), IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var file in Directory.EnumerateFiles(baseFolder, "*", options))
        {
            if (matcher(file))
            {
                yield return file;
            }
        }
    }

    /// <summary>A test of full paths against <paramref name="pattern"/>, relative to <paramref name="folder"/>.</summary>
    public static Func<string, bool> Matcher(string folder, string pattern)
    {
        var full = Path.GetFullPath(Path.Combine(folder, Normalize(pattern)));
        var parts = full.Split(Path.DirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path => Match(parts, 0, path.Split(Path.DirectorySeparatorChar), 0, comparison);
    }

    private static string Normalize(string path) => path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static bool Match(string[] pattern, int p, string[] path, int s, StringComparison comparison)
    {
        while (p < pattern.Length)
        {
            if (pattern[p] == "**")
            {
                for (int skip = s; skip <= path.Length; skip++)
                {
                    if (Match(pattern, p + 1, path, skip, comparison))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (s >= path.Length || !Segment(pattern[p], path[s], comparison))
            {
                return false;
            }

            p++;
            s++;
        }

        return s == path.Length;
    }

    private static bool Segment(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text, StringComparison comparison)
    {
        if (pattern.IndexOfAny('*', '?') < 0)
        {
            return pattern.Equals(text, comparison);
        }

        if (pattern.IsEmpty)
        {
            return text.IsEmpty;
        }

        if (pattern[0] == '*')
        {
            for (int i = 0; i <= text.Length; i++)
            {
                if (Segment(pattern[1..], text[i..], comparison))
                {
                    return true;
                }
            }

            return false;
        }

        if (text.IsEmpty)
        {
            return false;
        }

        bool same = pattern[0] == '?' || (comparison == StringComparison.Ordinal ? pattern[0] == text[0] : char.ToUpperInvariant(pattern[0]) == char.ToUpperInvariant(text[0]));
        return same && Segment(pattern[1..], text[1..], comparison);
    }
}
