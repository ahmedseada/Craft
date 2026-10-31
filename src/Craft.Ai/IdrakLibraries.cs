// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Reflection;
using System.Runtime.Loader;
using Idrak.Abstraction.Plugins;

namespace Craft.Ai;

/// <summary>
/// Hands Idrak's library assemblies that this plug-in carries to <see cref="IdrakPlugins.Load"/> (which includes their library
/// registrations, as the idrak tool's -P does). Idrak finds its
/// libraries' built-ins (model sources such as Hugging Face's, model formats, MCP transports) by loading them by name
/// in the default load context, where the host's Idrak.Abstraction lives; this plug-in's Idrak assemblies sit in its own
/// folder and load context, which the default context cannot see, so without this their built-ins never register
/// ("reads as a Hugging Face model id, but no source resolves those"). Once per process.
/// </summary>
internal static class IdrakLibraries
{
    private static readonly Lock Gate = new();
    private static bool s_included;

    public static void EnsureIncluded()
    {
        lock (Gate)
        {
            if (s_included)
            {
                return;
            }

            var own = typeof(IdrakLibraries).Assembly;
            var context = AssemblyLoadContext.GetLoadContext(own) ?? AssemblyLoadContext.Default;
            var folder = Path.GetDirectoryName(own.Location);
            if (!string.IsNullOrEmpty(folder))
            {
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
                            // A library (not a plug-in package: those register only when the idrakPlugins setting names them).
                            IdrakPlugins.Load(assembly);
                        }
                    }
                    catch (Exception e) when (e is FileLoadException or FileNotFoundException or BadImageFormatException)
                    {
                        // A file that is not a loadable assembly here registers nothing; the rest still do.
                    }
                }
            }

            s_included = true;
        }
    }
}
