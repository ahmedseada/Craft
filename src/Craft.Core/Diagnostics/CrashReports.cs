// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Craft.Core.Diagnostics;

/// <summary>
/// The crash reports: an exception nobody caught, written as text (when, where it was caught, the app and the system,
/// then the exception with its inner exceptions and stacks) to a file of its own in <see cref="CraftOptions.CrashFolder"/>,
/// so the app's crash dialog can name the file and the owner can paste it. Writing never throws.
/// </summary>
public static class CrashReports
{
    /// <summary>The folder reports go to: <see cref="CraftOptions.CrashFolder"/>, else local app data's Craft/crashes.</summary>
    public static string Folder(CraftOptions? options) =>
        options?.CrashFolder is { Length: > 0 } folder
            ? Path.GetFullPath(folder)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Craft", "crashes");

    /// <summary>The report's text for <paramref name="error"/>, caught by <paramref name="source"/>.</summary>
    public static string Format(Exception error, string source, string? extra = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(source);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Craft crash report, {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\n");
        text.Append(CultureInfo.InvariantCulture, $"Caught by: {source}\n");
        text.Append(CultureInfo.InvariantCulture, $"App: {typeof(CrashReports).Assembly.GetName().Version}, {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})\n");
        if (extra is { Length: > 0 })
        {
            text.Append(extra).Append('\n');
        }

        text.Append('\n').Append(error).Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// Writes the report of <paramref name="error"/> to a new file in <see cref="Folder"/>; returns its path, or null when
    /// it could not be written (the dialog then shows the text alone).
    /// </summary>
    public static string? Write(CraftOptions? options, Exception error, string source, string? extra = null)
    {
        try
        {
            var folder = Folder(options);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt"));
            File.WriteAllText(path, Format(error, source, extra));
            return path;
        }
#pragma warning disable CA1031 // A crash report that cannot be written must not hide the crash it reports.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}
