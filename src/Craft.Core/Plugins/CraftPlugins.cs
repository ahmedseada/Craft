// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using Idrak.Abstraction.Plugins;

namespace Craft.Core.Plugins;

/// <summary>What loading one plug-in folder did.</summary>
/// <param name="Name">The plug-in's folder name (its assembly's name).</param>
/// <param name="Folder">The folder.</param>
/// <param name="Loaded">Whether its assembly loaded and its plug-ins registered.</param>
/// <param name="Error">Why not, when it did not (disabled, missing, or the error it threw).</param>
public sealed record PluginLoadResult(string Name, string Folder, bool Loaded, string? Error);

/// <summary>
/// Loads Craft's plug-ins from folders: each plug-in is a folder <c>Name/</c> holding <c>Name.dll</c> (with its
/// <c>.deps.json</c> and its own dependencies), loaded into an <see cref="AssemblyLoadContext"/> of its own that shares
/// the host's assemblies (the contracts: Craft's and Idrak's, and anything the host has loaded) and takes the rest from
/// the plug-in's folder; then its package registers (<see cref="IdrakPlugins.Load"/>). A plug-in that fails to load or
/// to register is reported in its result and skipped: it never stops the host. Each folder's assembly loads once per
/// process; loading the folder again registers its plug-ins again if they were unregistered.
/// </summary>
public static class CraftPlugins
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, Assembly> Loaded = new(StringComparer.Ordinal);

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
                    ? new PluginLoadResult(name, folder, false, "disabled (CRAFT_PLUGINS_DISABLED)")
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
        lock (Gate)
        {
            if (Loaded.TryGetValue(dll, out var earlier))
            {
                return Register(name, folder, earlier);
            }

            if (!File.Exists(dll))
            {
                return new PluginLoadResult(name, folder, false, $"no {name}.dll in the folder");
            }

            Assembly assembly;
            try
            {
                assembly = new PluginLoadContext(dll).LoadFromAssemblyPath(dll);
            }
#pragma warning disable CA1031 // A plug-in's failure, whatever it is, is reported and the host goes on.
            catch (Exception error)
#pragma warning restore CA1031
            {
                return Failed(name, folder, error);
            }

            Loaded[dll] = assembly;
            return Register(name, folder, assembly);
        }
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

    /// <summary>A plug-in's own load context: the host's assemblies shared, the plug-in's dependencies from its folder.</summary>
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
