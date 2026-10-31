// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Craft.CSharp.Workspaces;

/// <summary>
/// The framework's assemblies a project compiles against. Where the .NET SDK's targeting packs are installed beside the
/// running runtime (<c>dotnet/packs/Microsoft.NETCore.App.Ref/VERSION/ref/netX.Y/</c>), those reference assemblies (with
/// their XML documentation); else the running runtime's own assemblies (the trusted platform assemblies of its folder):
/// implementation assemblies, which compile the same code (no documentation). Found once per framework and reused.
/// </summary>
public static class ReferenceAssemblies
{
    private static readonly ConcurrentDictionary<string, FrameworkReferences> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The running runtime's target framework ("net10.0").</summary>
    public static string RunningFramework { get; } = string.Create(CultureInfo.InvariantCulture, $"net{Environment.Version.Major}.{Environment.Version.Minor}");

    /// <summary>The folder of the running runtime (where System.Private.CoreLib is).</summary>
    public static string RuntimeFolder { get; } = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? RuntimeEnvironment.GetRuntimeDirectory();

    /// <summary>
    /// The reference assemblies of <paramref name="targetFramework"/> ("net10.0", "net10.0-windows10.0.19041.0"; null for
    /// the running one) and of the shared frameworks in <paramref name="frameworkReferences"/> ("Microsoft.AspNetCore.App";
    /// Microsoft.NETCore.App is always included).
    /// </summary>
    public static FrameworkReferences For(string? targetFramework, IEnumerable<string>? frameworkReferences = null)
    {
        string tfm = BaseFramework(targetFramework ?? RunningFramework);
        var frameworks = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "Microsoft.NETCore.App" };
        if (frameworkReferences is not null)
        {
            foreach (var name in frameworkReferences)
            {
                frameworks.Add(name);
            }
        }

        string key = tfm + "|" + string.Join(';', frameworks);
        return Cache.GetOrAdd(key, _ => Find(tfm, frameworks));
    }

    /// <summary>Forgets what was found (the next call looks again).</summary>
    public static void Clear() => Cache.Clear();

    /// <summary>"net10.0" of "net10.0-windows10.0.19041.0".</summary>
    public static string BaseFramework(string targetFramework)
    {
        ArgumentNullException.ThrowIfNull(targetFramework);
        int dash = targetFramework.IndexOf('-', StringComparison.Ordinal);
        return (dash < 0 ? targetFramework : targetFramework[..dash]).Trim();
    }

    private static FrameworkReferences Find(string tfm, SortedSet<string> frameworks)
    {
        var files = new List<string>();
        var analyzers = new List<string>();
        var notes = new List<string>();
        string? packs = PacksFolder();
        foreach (var framework in frameworks)
        {
            string? folder = packs is null ? null : RefFolder(Path.Combine(packs, framework + ".Ref"), tfm, notes);
            if (folder is not null)
            {
                files.AddRange(Directory.GetFiles(folder, "*.dll"));
                // ref/<tfm> is two folders under the pack's version folder, whose data/FrameworkList.xml lists its analyzers.
                analyzers.AddRange(PackAnalyzers(Path.GetFullPath(Path.Combine(folder, "..", ".."))));
            }
            else if (framework.Equals("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase))
            {
                files.AddRange(RuntimeAssemblies());
                notes.Add($"no targeting pack for {tfm}: the running runtime's assemblies are referenced (they compile the same code; no documentation)");
            }
            else
            {
                notes.Add($"no targeting pack for {framework} ({tfm}): its assemblies are not referenced");
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return new FrameworkReferences(files, notes) { Analyzers = analyzers };
    }

    /// <summary>
    /// The C# analyzers a targeting pack gives every project (its <c>data/FrameworkList.xml</c> entries of type Analyzer, in
    /// C# or in no language, as the SDK's <c>ResolveTargetingPackAssets</c> takes them); else its
    /// <c>analyzers/dotnet/cs</c> folder.
    /// </summary>
    internal static List<string> PackAnalyzers(string packVersionFolder)
    {
        var found = new List<string>();
        var list = Path.Combine(packVersionFolder, "data", "FrameworkList.xml");
        try
        {
            if (File.Exists(list))
            {
                var root = System.Xml.Linq.XDocument.Load(list).Root;
                foreach (var file in root?.Elements("File") ?? [])
                {
                    if ((string?)file.Attribute("Type") == "Analyzer" && (string?)file.Attribute("Path") is { } relative
                        && ((string?)file.Attribute("Language") is null or "cs" or "C#")
                        && relative.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        var path = Path.GetFullPath(Path.Combine(packVersionFolder, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
                        if (File.Exists(path))
                        {
                            found.Add(path);
                        }
                    }
                }

                return found;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            found.Clear();
        }

        var folder = Path.Combine(packVersionFolder, "analyzers", "dotnet", "cs");
        if (Directory.Exists(folder))
        {
            found.AddRange(Directory.GetFiles(folder, "*.dll"));
        }

        return found;
    }

    private static string? PacksFolder()
    {
        // RUNTIME = dotnet/shared/Microsoft.NETCore.App/VERSION, so dotnet = RUNTIME/../../..
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } set && Directory.Exists(Path.Combine(set, "packs"))
            ? set
            : Path.GetFullPath(Path.Combine(RuntimeFolder, "..", "..", ".."));
        var packs = Path.Combine(root, "packs");
        return Directory.Exists(packs) ? packs : null;
    }

    private static string? RefFolder(string pack, string tfm, List<string> notes)
    {
        if (!Directory.Exists(pack))
        {
            return null;
        }

        // The highest version that has ref/<tfm>; else the highest version's own ref folder, said so.
        var versions = Directory.GetDirectories(pack);
        Array.Sort(versions, static (a, b) => CompareVersions(Path.GetFileName(b), Path.GetFileName(a)));
        foreach (var version in versions)
        {
            var folder = Path.Combine(version, "ref", tfm);
            if (Directory.Exists(folder))
            {
                return folder;
            }
        }

        foreach (var version in versions)
        {
            var refs = Path.Combine(version, "ref");
            if (Directory.Exists(refs) && Directory.GetDirectories(refs) is [var only, ..])
            {
                notes.Add($"no targeting pack for {tfm} in {Path.GetFileName(pack)}: {Path.GetFileName(version)}'s {Path.GetFileName(only)} is referenced");
                return only;
            }
        }

        return null;
    }

    private static int CompareVersions(string a, string b)
    {
        int dashA = a.IndexOf('-', StringComparison.Ordinal);
        int dashB = b.IndexOf('-', StringComparison.Ordinal);
        bool okA = Version.TryParse(dashA < 0 ? a : a[..dashA], out var va);
        bool okB = Version.TryParse(dashB < 0 ? b : b[..dashB], out var vb);
        if (okA && okB && va != vb)
        {
            return va!.CompareTo(vb);
        }

        // A release sorts above its previews.
        if (okA && okB && (dashA < 0) != (dashB < 0))
        {
            return dashA < 0 ? 1 : -1;
        }

        return string.CompareOrdinal(a, b);
    }

    private static List<string> RuntimeAssemblies()
    {
        var folder = Path.TrimEndingDirectorySeparator(RuntimeFolder);
        var files = new List<string>();
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string list)
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            foreach (var range in list.AsSpan().Split(Path.PathSeparator))
            {
                var path = list.AsSpan()[range];
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(path).Equals(folder, comparison))
                {
                    files.Add(path.ToString());
                }
            }

            return files;
        }

        files.AddRange(Directory.EnumerateFiles(folder, "*.dll"));
        return files;
    }
}

/// <summary>A framework's reference assemblies, and what could not be found as the build would.</summary>
/// <param name="Files">The assemblies, as full paths.</param>
/// <param name="Notes">What was taken instead of the targeting pack, if anything.</param>
public sealed record FrameworkReferences(IReadOnlyList<string> Files, IReadOnlyList<string> Notes)
{
    /// <summary>The C# analyzer assemblies the targeting packs give every project (their source generators among them).</summary>
    public IReadOnlyList<string> Analyzers { get; init; } = [];
}
