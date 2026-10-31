// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Reflection;
using System.Runtime.Loader;
using Idrak.Abstraction.Plugins;

namespace Craft.Abstractions;

/// <summary>
/// The Idrak libraries a plug-in carries in its folder (Idrak, Idrak.Text, Idrak.Data, …), handed to Idrak's
/// <see cref="IdrakPlugins.Load"/> so their built-ins register (model sources such as Hugging Face's, model and image
/// formats, the GPU backends of Idrak.Gpu), as the idrak tool's -P does. Idrak finds its libraries' built-ins by loading
/// them by name in the default load context, which cannot see a plug-in's folder, so without this a plug-in's model never
/// finds its source or its GPU. Call it at activation, before the first model; once per plug-in assembly, any thread.
/// </summary>
public static class PluginIdrakLibraries
{
    private static readonly Lock Gate = new();
    private static readonly HashSet<string> Included = new(StringComparer.Ordinal);

    /// <summary>
    /// Includes the Idrak libraries in <paramref name="plugin"/>'s folder (not the plug-in packages among them: those
    /// register when a setting names them, through <see cref="IncludePackages"/>). A file that is not a loadable assembly
    /// here registers nothing; the others still do.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "A plug-in is an assembly loaded from its own folder, never one embedded in a single-file app; an empty location includes nothing.")]
    public static void Include(Assembly plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        lock (Gate)
        {
            if (!Included.Add(plugin.FullName ?? plugin.GetName().Name ?? ""))
            {
                return;
            }

            var context = AssemblyLoadContext.GetLoadContext(plugin) ?? AssemblyLoadContext.Default;
            if (Path.GetDirectoryName(plugin.Location) is not { Length: > 0 } folder)
            {
                return;
            }

            var files = Directory.GetFiles(folder, "Idrak*.dll");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (name == "Idrak.Abstraction")
                {
                    continue;                                       // the host's, already the library's own
                }

                try
                {
                    var assembly = context.LoadFromAssemblyName(new AssemblyName(name));
                    if (IdrakPlugins.PackageOf(assembly) is null)
                    {
                        IdrakPlugins.Load(assembly);
                    }
                }
                catch (Exception e) when (e is FileLoadException or FileNotFoundException or BadImageFormatException)
                {
                    // Not a loadable assembly here: it registers nothing.
                }
            }
        }
    }

    /// <summary>
    /// Registers the Idrak plug-in packages <paramref name="names"/> (the model families: "Idrak.Text.Plugins") from
    /// <paramref name="plugin"/>'s load context, after its libraries (<see cref="Include"/>); the packages registered.
    /// </summary>
    /// <exception cref="FileNotFoundException">A package is not in the plug-in's folder (the message names it).</exception>
    public static IReadOnlyList<IIdrakPluginPackage> IncludePackages(Assembly plugin, IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(names);
        Include(plugin);
        var context = AssemblyLoadContext.GetLoadContext(plugin) ?? AssemblyLoadContext.Default;
        var packages = new List<IIdrakPluginPackage>();
        foreach (var name in names)
        {
            var assembly = context.LoadFromAssemblyName(new AssemblyName(name));
            IdrakPlugins.Load(assembly);                            // registers nothing twice
            if (IdrakPlugins.PackageOf(assembly) is { } package)
            {
                packages.Add(package);
            }
        }

        return packages;
    }
}
