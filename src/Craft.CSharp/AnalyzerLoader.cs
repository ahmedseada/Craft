// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Craft.CSharp;

/// <summary>
/// Loads the projects' analyzer assemblies (for their source generators) once per process, into one load context of its
/// own whose parent is the plug-in's: Roslyn and what the plug-in carries (Microsoft.CodeAnalysis*, System.Collections.Immutable,
/// System.Reflection.Metadata) resolve to the plug-in's own copies, so a generator built against an older Roslyn binds to
/// the one the workspace runs; an analyzer's own dependencies resolve from its folder (a targeting pack's
/// Microsoft.Interop.SourceGeneration beside LibraryImportGenerator); the framework from the default context. An assembly
/// is read once per path; its reference (<see cref="AnalyzerFileReference"/>) is kept per path and write time. Never
/// unloaded: a generator's types live as long as the process, like the compiler server's.
/// </summary>
internal sealed class AnalyzerLoader : IAnalyzerAssemblyLoader
{
    public static readonly AnalyzerLoader Instance = new();

    private static readonly ConcurrentDictionary<string, (DateTime Written, AnalyzerFileReference Reference)> References = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, string> _dependencies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Assembly>> _loaded = new(StringComparer.Ordinal);
    private readonly Context _context;

    private AnalyzerLoader()
    {
        var parent = AssemblyLoadContext.GetLoadContext(typeof(AnalyzerLoader).Assembly) ?? AssemblyLoadContext.Default;
        _context = new Context(this, parent);
    }

    /// <summary>The analyzer assemblies loaded so far (the telemetry and the tests).</summary>
    public int LoadedCount => _loaded.Count;

    /// <summary>The load context the analyzers live in.</summary>
    public AssemblyLoadContext LoadContext => _context;

    /// <summary>The reference of the analyzer assembly at <paramref name="path"/>, made once per path and write time; null when it is gone.</summary>
    public static AnalyzerFileReference? Reference(string path)
    {
        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (References.TryGetValue(path, out var cached) && cached.Written == written)
        {
            return cached.Reference;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        // Its folder's other assemblies are its dependencies (an analyzer package's helpers).
        foreach (var dependency in Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.dll"))
        {
            Instance.AddDependencyLocation(dependency);
        }

        var reference = new AnalyzerFileReference(path, Instance);
        References[path] = (written, reference);
        return reference;
    }

    /// <inheritdoc />
    public void AddDependencyLocation(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        if (!fullPath.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
        {
            _dependencies.TryAdd(Path.GetFileNameWithoutExtension(fullPath), fullPath);
        }
    }

    /// <inheritdoc />
    public Assembly LoadFromPath(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        AddDependencyLocation(fullPath);
        return _loaded.GetOrAdd(fullPath, path => new Lazy<Assembly>(() => _context.LoadFromAssemblyPath(path), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private Assembly? Resolve(AssemblyName name, AssemblyLoadContext parent)
    {
        if (name.Name is not { } simple)
        {
            return null;
        }

        // Shared with the plug-in: Roslyn and its own dependencies, whatever version the analyzer was built against.
        if (IsShared(simple))
        {
            return parent.LoadFromAssemblyName(new AssemblyName(simple));
        }

        if (_dependencies.TryGetValue(simple, out var path))
        {
            return LoadFromPath(path);
        }

        // Something else the plug-in carries; else the framework (the default context, by returning null).
        if (!ReferenceEquals(parent, Default))
        {
            try
            {
                return parent.LoadFromAssemblyName(name);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        return null;
    }

    private static AssemblyLoadContext Default => AssemblyLoadContext.Default;

    private static bool IsShared(string simple) =>
        simple.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
        || simple is "System.Collections.Immutable" or "System.Reflection.Metadata";

    private sealed class Context(AnalyzerLoader loader, AssemblyLoadContext parent) : AssemblyLoadContext("Craft.CSharp analyzers", isCollectible: false)
    {
        protected override Assembly? Load(AssemblyName assemblyName) => loader.Resolve(assemblyName, parent);
    }
}
