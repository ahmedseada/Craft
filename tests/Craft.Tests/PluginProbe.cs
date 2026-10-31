// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Runtime.Loader;
using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Plugins;
using Craft.Editor;
using Idrak.Abstraction.Devices;
using Idrak.Abstraction.Generation;

namespace Craft.Tests;

/// <summary>
/// A plug-in folder loaded in a fresh process (the test program run with <c>--probe-plugin FOLDER</c>, before it discovers
/// its tests, so nothing of a plug-in's Idrak libraries is in the default load context, as in the app): it loads the
/// folder through CraftPlugins, activates every plug-in in a host, and prints what the plug-in's own registries hold,
/// one "key: value" line each. It names no type of the plug-in's libraries: the voice plug-in describes its own.
/// </summary>
internal static class PluginProbe
{
    public static int Run(string folder)
    {
        var result = CraftPlugins.LoadFolder(folder);
        Console.WriteLine($"loaded: {result.Loaded} {result.Error}");
        using var host = new EditorHost(readVariable: Environment.GetEnvironmentVariable);
        foreach (var activation in host.RefreshPlugins())
        {
            Console.WriteLine($"activation {activation.Name}: {activation.Active} {activation.Error}");
        }

        foreach (var plugin in EditorPlugins.All)
        {
            if (plugin.GetType().GetMethod("DescribeRegistries")?.Invoke(null, null) is IReadOnlyList<string> lines)
            {
                foreach (var line in lines)
                {
                    Console.WriteLine(line);
                }
            }
        }

        Console.WriteLine("Idrak.Audio in the default context: " + AssemblyLoadContext.Default.Assemblies.Any(a => a.GetName().Name == "Idrak.Audio"));
        Console.WriteLine("chat parts: " + string.Join(", ", ChatParts.Names));
        Console.WriteLine("audio chat part from: " + (ChatParts.Find(ChatParts.Audio) is { } audio ? AssemblyLoadContext.GetLoadContext(audio.GetType().Assembly)?.Name : "(none)"));
        Console.WriteLine("device providers: " + string.Join(", ", DeviceProviders.All.Select(p => p.GetType().Name)));
        Console.WriteLine("device provider names: " + string.Join(", ", DeviceProviders.All.Select(p => p.Name)));
        Console.WriteLine("device providers from: " + string.Join(", ", DeviceProviders.All.Select(p => $"{p.Name}={AssemblyLoadContext.GetLoadContext(p.GetType().Assembly)?.Name}")));
        Console.WriteLine("default device: " + Idrak.Abstraction.Device.Default);
        Console.WriteLine("commands: " + string.Join(", ", EditorCommands.Names));
        Console.WriteLine("key bindings: " + string.Join(", ", EditorKeyBindings.All.Select(b => $"{b.Name}={b.CommandId}")));
        return 0;
    }

    /// <summary>
    /// Every plug-in under <paramref name="root"/> (a <c>plugins</c> folder laid out as Craft.App's targets lay it out),
    /// loaded as the app loads them (<see cref="CraftPlugins.LoadInstalled"/>, in name order, Idrak's libraries shared),
    /// then activated in a host: one "plugin NAME: LOADED ERROR" line each, then what the registries hold.
    /// </summary>
    public static int RunInstalled(string root)
    {
        foreach (var result in CraftPlugins.LoadInstalled(new CraftOptions { PluginFolders = [root] }))
        {
            Console.WriteLine($"plugin {result.Name}: {result.Loaded} {result.Error}");
        }

        using var host = new EditorHost(readVariable: _ => null);
        foreach (var activation in host.RefreshPlugins())
        {
            Console.WriteLine($"activation {activation.Name}: {activation.Active} {activation.Error}");
        }

        Console.WriteLine("language .cs: " + (EditorLanguages.ForFile("a.cs")?.Name ?? "(none)"));
        Console.WriteLine("language .csx: " + (EditorLanguages.ForFile("a.csx")?.Name ?? "(none)"));
        Console.WriteLine("editor plugins: " + string.Join(", ", EditorPlugins.Names));
        Console.WriteLine("tool sets: " + string.Join(", ", EditorToolSets.Names));
        Console.WriteLine("idrak plugins: " + string.Join(", ", Idrak.Abstraction.Plugins.IdrakPlugins.Names.Order(StringComparer.Ordinal)
            .Select(n => $"{n}={Idrak.Abstraction.Plugins.IdrakPlugins.Find(n)?.GetType().Assembly.GetName().Name}")));
        return 0;
    }

    /// <summary>The probe's lines for <paramref name="folder"/>, from a fresh run of this test program.</summary>
    public static IReadOnlyList<string> RunInFreshProcess(string folder) => RunInFreshProcess("--probe-plugin", folder);

    /// <summary>The probe's lines for the plug-ins under <paramref name="root"/> (<see cref="RunInstalled"/>), from a fresh run of this test program.</summary>
    public static IReadOnlyList<string> RunInstalledInFreshProcess(string root) => RunInFreshProcess("--probe-installed", root);

    private static List<string> RunInFreshProcess(string mode, string folder)
    {
        var program = Environment.ProcessPath!;
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(program).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(PluginProbe).Assembly.Location);
        }

        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(folder);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The plug-in probe did not finish within 120 s.");
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (process.ExitCode != 0)
        {
            lines.Add($"exit: {process.ExitCode} {error.Result}");
        }

        return lines;
    }
}
