// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Reflection;
using System.Runtime.Loader;
using Idrak.Abstraction.Plugins;
using Idrak.Reinforcement.Plugins.CSharp;

namespace Craft.Plugins.Learning;

/// <summary>
/// Hands the Idrak libraries this plug-in carries (Idrak, Idrak.Gpu, Idrak.Text, Idrak.Reinforcement, Idrak.Data, …) to
/// <see cref="IdrakPlugins.Load"/>, so their built-ins register (the GPU devices "auto" chooses, the model sources, the
/// reinforcement trainer's losses and estimators, the preference losses): Idrak finds them by loading its libraries in the
/// default load context, which cannot see a plug-in's folder (Craft's loader shares one copy of each between the plug-ins).
/// Then the C# coding environment of Idrak.Reinforcement.Plugins (the plug-in "csharp-coding", its environment "csharp", with "coding" and Idrak.Text.Plugins' C#
/// language it requires): an Idrak plug-in package, registered by name rather than by loading the whole package. Once per
/// process; <see cref="Release"/> takes back what this registered (the libraries' built-ins stay, as Craft.Ai's do).
/// </summary>
internal static class LearningLibraries
{
    private static readonly Lock Gate = new();
    private static bool s_included;
    private static bool s_registeredCSharp;

    public static void EnsureIncluded()
    {
        lock (Gate)
        {
            if (!s_included)
            {
                var own = typeof(LearningLibraries).Assembly;
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
                            continue;                                 // the host's
                        }

                        try
                        {
                            var assembly = context.LoadFromAssemblyName(new AssemblyName(name));
                            if (IdrakPlugins.PackageOf(assembly) is null)
                            {
                                IdrakPlugins.Load(assembly);          // a library, not a plug-in package
                            }
                        }
                        catch (Exception e) when (e is FileLoadException or FileNotFoundException or BadImageFormatException)
                        {
                            // A file that does not load here registers nothing; the rest still do.
                        }
                    }
                }

                s_included = true;
            }

            if (!CSharpCodingPlugin.IsRegistered)
            {
                CSharpCodingPlugin.Register();
                s_registeredCSharp = true;
            }
        }
    }

    public static void Release()
    {
        lock (Gate)
        {
            if (s_registeredCSharp)
            {
                CSharpCodingPlugin.Unregister();
                s_registeredCSharp = false;
            }
        }
    }
}
