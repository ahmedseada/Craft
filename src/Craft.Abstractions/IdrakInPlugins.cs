// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using Idrak.Abstraction.Plugins;

namespace Craft.Abstractions;

/// <summary>
/// Idrak's assemblies that a plug-in carries in its own folder, registered from the plug-in's own load context. Idrak
/// finds its libraries' built-ins (model sources such as Hugging Face's, model formats, the "audio" chat part) by loading
/// them by name in the default load context, where the host's Idrak.Abstraction lives; a plug-in's Idrak assemblies sit
/// in its folder and load context, which the default context cannot see, so without this their built-ins never register
/// (the owner's first Windows run: "reads as a Hugging Face model id, but no source resolves those").
/// <see cref="IncludeLibraries"/> hands each library to <see cref="IdrakPlugins.Load"/> (as the idrak tool's -P does),
/// leaving out plug-in packages (the model families), which <see cref="IncludePackages"/> registers by name when the
/// plug-in's setting names them. Each once per plug-in assembly.
/// </summary>
public static class IdrakInPlugins
{
    private static readonly Lock Gate = new();
    private static readonly HashSet<Assembly> Included = [];
    private static readonly HashSet<(Assembly, string)> Packages = [];

    /// <summary>
    /// Registers every Idrak library (Idrak*.dll beside <paramref name="plugin"/>, not a plug-in package, not the host's
    /// Idrak.Abstraction) loaded in <paramref name="plugin"/>'s own load context; once per plug-in assembly.
    /// </summary>
    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "A plug-in is an assembly loaded from its own folder, never bundled into a single file.")]
    public static void IncludeLibraries(Assembly plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        lock (Gate)
        {
            if (!Included.Add(plugin))
            {
                return;
            }

            var context = AssemblyLoadContext.GetLoadContext(plugin) ?? AssemblyLoadContext.Default;
            var folder = Path.GetDirectoryName(plugin.Location);
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            var files = Directory.GetFiles(folder, "Idrak*.dll");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name == "Idrak.Abstraction")
                {
                    continue;                                     // the host's, already the library's own
                }

                try
                {
                    var assembly = context.LoadFromAssemblyName(new AssemblyName(name));
                    if (IdrakPlugins.PackageOf(assembly) is null)
                    {
                        IdrakPlugins.Load(assembly);                 // a library: its built-ins as the registries' defaults
                    }
                }
                catch (Exception e) when (e is FileLoadException or FileNotFoundException or BadImageFormatException)
                {
                    // A file that is not a loadable assembly here registers nothing; the rest still do.
                }
            }
        }
    }

    /// <summary>
    /// Registers the Idrak plug-in packages named <paramref name="packages"/> ("Idrak.Audio.Plugins"), each loaded in
    /// <paramref name="plugin"/>'s own load context beside its Idrak; once per plug-in assembly and name.
    /// </summary>
    /// <exception cref="FileNotFoundException">A named package is not beside the plug-in.</exception>
    public static void IncludePackages(Assembly plugin, IEnumerable<string> packages)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(packages);
        lock (Gate)
        {
            var context = AssemblyLoadContext.GetLoadContext(plugin) ?? AssemblyLoadContext.Default;
            foreach (var name in packages)
            {
                if (!string.IsNullOrWhiteSpace(name) && Packages.Add((plugin, name.Trim())))
                {
                    IdrakPlugins.Load(context.LoadFromAssemblyName(new AssemblyName(name.Trim())));
                }
            }
        }
    }
}
