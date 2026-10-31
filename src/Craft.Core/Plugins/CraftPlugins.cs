// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using Craft.Abstractions;
using Idrak.Abstraction.Plugins;

namespace Craft.Core.Plugins;

/// <summary>What loading one plug-in folder did.</summary>
/// <param name="Name">The plug-in's folder name (its assembly's name).</param>
/// <param name="Folder">The folder.</param>
/// <param name="Loaded">Whether its assembly loaded and its plug-ins registered.</param>
/// <param name="Error">Why not, when it did not (disabled, missing, refused by its plugin.json, or the error it threw).</param>
public sealed record PluginLoadResult(string Name, string Folder, bool Loaded, string? Error)
{
    /// <summary>Its <c>plugin.json</c>, or null when the folder has none (it still loads) or the file was refused.</summary>
    public PluginManifest? Manifest { get; init; }

    /// <summary>Whether it was skipped because it is disabled (<c>CRAFT_PLUGINS_DISABLED</c>).</summary>
    public bool Disabled { get; init; }

    /// <summary>Whether it was built against an older major contract version than this Craft's (loaded and marked).</summary>
    public bool Outdated { get; init; }

    /// <summary>What to know about a plug-in that loaded (no plugin.json, built against an older contract), or null.</summary>
    public string? Note { get; init; }

    /// <summary>Its version from its plugin.json, or null.</summary>
    public string? Version => Manifest?.Version;

    /// <summary>Its summary from its plugin.json, or null.</summary>
    public string? Summary => Manifest?.Summary;
}

/// <summary>
/// Loads Craft's plug-ins from folders: each plug-in is a folder <c>Name/</c> holding <c>Name.dll</c> (with its
/// <c>.deps.json</c> and its own dependencies), loaded into an <see cref="AssemblyLoadContext"/> of its own that shares
/// the host's assemblies (the contracts: Craft's and Idrak's, and anything the host has loaded) and takes the rest from
/// the plug-in's folder; then its package registers (<see cref="IdrakPlugins.Load"/>). A plug-in that fails to load or
/// to register is reported in its result and skipped: it never stops the host. Each folder's assembly loads once per
/// process; loading the folder again registers its plug-ins again if they were unregistered. A folder's
/// <c>plugin.json</c> (<see cref="PluginManifest"/>), when it has one, is read first: a file that does not parse or check
/// out, or a contract version newer than this Craft's, refuses the plug-in with the reason before any of its code loads;
/// an older major is loaded and marked <see cref="PluginLoadResult.Outdated"/>. A folder without one still loads, said so.
/// </summary>
public static class CraftPlugins
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Assembly> Loaded = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PluginLoadResult> Results = new(StringComparer.Ordinal);
    private static readonly List<string> ResultOrder = [];

    /// <summary>
    /// The latest result of every plug-in folder this process loaded or skipped (by <see cref="LoadInstalled"/> or
    /// <see cref="LoadFolder"/>), in the order first seen: what the Plug-ins page lists.
    /// </summary>
    public static IReadOnlyList<PluginLoadResult> Installed
    {
        get
        {
            lock (Gate)
            {
                return [.. ResultOrder.Select(folder => Results[folder])];
            }
        }
    }

    /// <summary>The folder of the plug-ins installed with the app: <c>plugins</c> beside it.</summary>
    public static string InstalledFolder => Path.Combine(AppContext.BaseDirectory, "plugins");

    /// <summary>
    /// Loads every plug-in of the app's <see cref="InstalledFolder"/> (shipped with it), then of each of
    /// <see cref="CraftOptions.PluginFolders"/>, in name order within a folder, skipping
    /// <see cref="CraftOptions.DisabledPlugins"/>; returns a result for each plug-in folder found.
    /// </summary>
    [RequiresUnreferencedCode("A plug-in is loaded and its package made by reflection.")]
    public static IReadOnlyList<PluginLoadResult> LoadInstalled(CraftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var disabled = new HashSet<string>(options.DisabledPlugins, StringComparer.OrdinalIgnoreCase);
        var results = new List<PluginLoadResult>();
        foreach (var root in (IEnumerable<string>)[InstalledFolder, .. options.PluginFolders])
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var folders = Directory.GetDirectories(root);
            Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                var name = Path.GetFileName(folder);
                results.Add(disabled.Contains(name)
                    ? Note(new PluginLoadResult(name, folder, false, "disabled (CRAFT_PLUGINS_DISABLED)") { Disabled = true, Manifest = TryManifest(folder, name) })
                    : LoadFolder(folder));
            }
        }

        return results;
    }

    /// <summary>Loads the plug-in in <paramref name="pluginFolder"/> (a folder <c>Name/</c> holding <c>Name.dll</c>).</summary>
    [RequiresUnreferencedCode("A plug-in is loaded and its package made by reflection.")]
    public static PluginLoadResult LoadFolder(string pluginFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginFolder);
        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pluginFolder));
        var name = Path.GetFileName(folder);
        var dll = Path.Combine(folder, name + ".dll");
        PluginLoadResult result;
        lock (Gate)
        {
            result = Load(name, folder, dll);
        }

        return Note(result);
    }

    [RequiresUnreferencedCode("A plug-in is loaded and its package made by reflection.")]
    private static PluginLoadResult Load(string name, string folder, string dll)
    {
        if (!File.Exists(dll))
        {
            return new PluginLoadResult(name, folder, false, $"no {name}.dll in the folder");
        }

        // The manifest is checked before any of the plug-in's code loads: a refused plug-in is never loaded half-way.
        var check = CheckManifest(name, folder);
        if (check.Refusal is not null)
        {
            return check.Refusal;
        }

        if (!Loaded.TryGetValue(dll, out var assembly))
        {
            try
            {
                assembly = new PluginLoadContext(dll).LoadFromAssemblyPath(dll);
            }
#pragma warning disable CA1031 // A plug-in's failure, whatever it is, is reported and the host goes on.
            catch (Exception error)
#pragma warning restore CA1031
            {
                return Failed(name, folder, error) with { Manifest = check.Manifest };
            }

            Loaded[dll] = assembly;
        }

        var result = Register(name, folder, assembly) with { Manifest = check.Manifest, Outdated = check.Outdated };
        return result.Loaded ? result with { Note = check.Note } : result;
    }

    private static (PluginManifest? Manifest, bool Outdated, string? Note, PluginLoadResult? Refusal) CheckManifest(string name, string folder)
    {
        var path = Path.Combine(folder, PluginManifest.FileName);
        if (!File.Exists(path))
        {
            return (null, false, $"no {PluginManifest.FileName}: loaded without a version or a contract version to check", null);
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return (null, false, null, new PluginLoadResult(name, folder, false, $"{PluginManifest.FileName} could not be read: {error.Message}"));
        }

        if (PluginManifest.Read(bytes, name, out var problems) is not { } manifest)
        {
            return (null, false, null, new PluginLoadResult(name, folder, false, $"refused: {string.Join("; ", problems)}"));
        }

        var builtAgainst = CraftContracts.Parse(manifest.ContractVersion)!;
        var running = CraftContracts.Version.ToString(2);
        return CraftContracts.Fit(builtAgainst) switch
        {
            ContractFit.Newer => (manifest, false, null, new PluginLoadResult(name, folder, false,
                $"refused: built against Craft contract {builtAgainst.ToString(2)}, newer than this Craft's {running}; update Craft or use a version of the plug-in built for {running}")
            {
                Manifest = manifest,
            }),
            ContractFit.Outdated => (manifest, true, $"built against Craft contract {builtAgainst.ToString(2)}, older than this Craft's {running}: marked outdated", null),
            _ => (manifest, false, null, null),
        };
    }

    private static PluginManifest? TryManifest(string folder, string name)
    {
        try
        {
            var path = Path.Combine(folder, PluginManifest.FileName);
            return File.Exists(path) ? PluginManifest.Read(File.ReadAllBytes(path), name, out _) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static PluginLoadResult Note(PluginLoadResult result)
    {
        lock (Gate)
        {
            if (!Results.ContainsKey(result.Folder))
            {
                ResultOrder.Add(result.Folder);
            }

            Results[result.Folder] = result;
        }

        return result;
    }

    [RequiresUnreferencedCode("A plug-in is loaded and its package made by reflection.")]
    private static PluginLoadResult Register(string name, string folder, Assembly assembly)
    {
        try
        {
            if (IdrakPlugins.PackageOf(assembly) is not { } package)
            {
                return IdrakPlugins.Load(assembly)
                    ? new PluginLoadResult(name, folder, true, null)
                    : new PluginLoadResult(name, folder, false, $"{name}.dll declares no plug-in package ([assembly: IdrakPluginPackage(...)])");
            }

            IdrakPlugins.Load(assembly);
            IdrakPlugins.RegisterAll(package);
            return new PluginLoadResult(name, folder, true, null);
        }
#pragma warning disable CA1031 // A plug-in's failure, whatever it is, is reported and the host goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            return Failed(name, folder, error);
        }
    }

    private static PluginLoadResult Failed(string name, string folder, Exception error)
    {
        var inner = error is TargetInvocationException { InnerException: { } cause } ? cause : error;
        return new PluginLoadResult(name, folder, false, $"{inner.GetType().Name}: {inner.Message}");
    }

    /// <summary>
    /// The Idrak libraries the plug-ins carry (Idrak, Idrak.Text, Idrak.Data, …), each loaded once per process by the first
    /// plug-in that needs it and handed to every other: a library's registries are statics, so two copies would be two
    /// registries, and a model family one plug-in registers (P2) would be invisible to the provider of another (Craft.Ai).
    /// </summary>
    private static readonly Dictionary<string, Assembly> IdrakLibraries = new(StringComparer.Ordinal);
    private static readonly Lock IdrakLibrariesGate = new();

    /// <summary>Whether <paramref name="name"/> is one of Idrak's libraries (shared between the plug-ins, one copy each).</summary>
    internal static bool IsIdrakLibrary(string? name) => name is not null && (name == "Idrak" || name.StartsWith("Idrak.", StringComparison.Ordinal));

    /// <summary>The Idrak libraries loaded for the plug-ins so far, by name, and the load context each lives in (the Plug-ins page and the tests).</summary>
    public static IReadOnlyDictionary<string, string> SharedIdrakLibraries
    {
        get
        {
            lock (IdrakLibrariesGate)
            {
                return IdrakLibraries.ToDictionary(p => p.Key, p => AssemblyLoadContext.GetLoadContext(p.Value)?.Name ?? "", StringComparer.Ordinal);
            }
        }
    }

    /// <summary>A plug-in's own load context: the host's assemblies shared, Idrak's libraries shared between plug-ins, the plug-in's other dependencies from its folder.</summary>
    private sealed class PluginLoadContext(string dll) : AssemblyLoadContext(Path.GetFileNameWithoutExtension(dll), isCollectible: false)
    {
        private readonly AssemblyDependencyResolver _resolver = new(dll);

        [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The context is made only by LoadFolder, which requires unreferenced code.")]
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (IsShared(assemblyName))
            {
                return null;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            if (IsIdrakLibrary(assemblyName.Name))
            {
                lock (IdrakLibrariesGate)
                {
                    if (IdrakLibraries.TryGetValue(assemblyName.Name!, out var loaded))
                    {
                        return loaded;
                    }

                    if (path is null || !File.Exists(path))
                    {
                        return null;
                    }

                    var library = LoadFromAssemblyPath(path);
                    IdrakLibraries[assemblyName.Name!] = library;
                    return library;
                }
            }

            return path is not null && File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }

        private static bool IsShared(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (name is null)
            {
                return false;
            }

            // The contracts come from the host, so the plug-in and the host see the same types.
            if (name == "Idrak.Abstraction" || (name.StartsWith("Craft.", StringComparison.Ordinal) && File.Exists(Path.Combine(AppContext.BaseDirectory, name + ".dll"))))
            {
                return true;
            }

            foreach (var loaded in Default.Assemblies)
            {
                if (string.Equals(loaded.GetName().Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
