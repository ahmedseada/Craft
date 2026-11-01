// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Craft.Abstractions;
using Craft.Core.Diagnostics;

namespace Craft.Editor;

/// <summary>
/// The host's <see cref="IEditorDiagnostics"/>: the app's version, the crash folder of <c>CRAFT_CRASH_FOLDER</c> (its
/// crash, callback and stall reports), the crash trail the host records (<see cref="EditorHost.Trail"/>), Output's last
/// lines (<see cref="EditorHost.RecentLog"/>), and the telemetry table the window keeps (<see cref="MeasureSource"/>).
/// </summary>
public sealed class HostDiagnostics(EditorHost host) : IEditorDiagnostics
{
    private readonly EditorHost _host = host ?? throw new ArgumentNullException(nameof(host));

    /// <summary>Reads the window's telemetry table (set by the window, on the UI thread); null: none kept.</summary>
    public Func<IReadOnlyList<TelemetryMeasure>>? MeasureSource { get; set; }

    /// <inheritdoc />
    public string AppVersion
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(HostDiagnostics).Assembly;
            string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString() ?? "unknown";
            return string.Create(CultureInfo.InvariantCulture,
                $"{assembly.GetName().Name} {version} · {RuntimeInformation.FrameworkDescription} · {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})");
        }
    }

    /// <inheritdoc />
    public string? ReportFolder => CrashReports.Folder(_host.Options);

    /// <inheritdoc />
    public IReadOnlyList<string> TrailLines(int max) =>
        max <= 0 || _host.Trail is not { } trail ? [] : CrashTrail.Read(trail.Path, max);

    /// <inheritdoc />
    public IReadOnlyList<string> OutputLines(int max)
    {
        var log = _host.RecentLog;
        int from = Math.Max(0, log.Count - Math.Max(0, max));
        var lines = new List<string>(log.Count - from);
        for (int i = from; i < log.Count; i++)
        {
            var entry = log[i];
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{entry.Time:HH:mm:ss} {entry.Level} {entry.Source}: {entry.Message}")
                + (entry.Error is { } error ? $" ({error.GetType().Name}: {error.Message})" : ""));
        }

        return lines;
    }

    /// <inheritdoc />
    public IReadOnlyList<TelemetryMeasure> Measures(int max)
    {
        if (max <= 0 || MeasureSource is not { } source)
        {
            return [];
        }

        var all = new List<TelemetryMeasure>(source());
        all.Sort(static (a, b) => b.P95.CompareTo(a.P95));
        return all.Count <= max ? all : all.GetRange(0, max);
    }
}
