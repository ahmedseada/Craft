// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using Craft.Abstractions;
using Microsoft.Win32;

namespace Craft.Maui.WinUI;

/// <summary>
/// B9 on Windows: the library's profile rules (<see cref="MachineTerminalProfiles"/>) over this machine with what only the
/// registry tells: the WSL distributions (HKCU\Software\Microsoft\Windows\CurrentVersion\Lxss\{id}\DistributionName) and
/// Git for Windows' folder (HKLM\SOFTWARE\GitForWindows\InstallPath). Registered as "machine" over the library's while
/// the shell runs; read off the UI thread by <see cref="TerminalProfiles.DiscoverAsync"/>.
/// </summary>
public static class WindowsTerminalProfiles
{
    /// <summary>The source: this machine with its registry.</summary>
    public static ITerminalProfileSource Create() => new MachineTerminalProfiles(TerminalProfiles.Machine, new TerminalMachine
    {
        IsWindows = true,
        Variable = Environment.GetEnvironmentVariable,
        FileExists = File.Exists,
        Folders = TerminalMachine.Local.Folders,
        WslDistributions = WslDistributions,
        GitFolder = GitFolder,
    });

    private static List<string> WslDistributions()
    {
        using var lxss = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Lxss");
        if (lxss is null)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var id in lxss.GetSubKeyNames())
        {
            using var distribution = lxss.OpenSubKey(id);
            if (distribution?.GetValue("DistributionName") is string name && name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static string? GitFolder()
    {
        foreach (var hive in (ReadOnlySpan<RegistryKey>)[Registry.LocalMachine, Registry.CurrentUser])
        {
            using var git = hive.OpenSubKey(@"SOFTWARE\GitForWindows");
            if (git?.GetValue("InstallPath") is string path && path.Length > 0)
            {
                return path;
            }
        }

        return null;
    }
}
#endif
