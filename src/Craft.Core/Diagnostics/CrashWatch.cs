// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace Craft.Core.Diagnostics;

/// <summary>
/// A run's marker (<c>run-&lt;pid&gt;.json</c> in the crash folder): its process, when it started, the app's version and
/// its breadcrumb trail's file. Written at start and removed on a clean exit, so a marker the next start finds for a
/// process that is gone is a run that died, whatever killed it.
/// </summary>
/// <param name="ProcessId">The process's id.</param>
/// <param name="ProcessName">The process's name (Craft.App).</param>
/// <param name="Started">When it started.</param>
/// <param name="Version">The app's version.</param>
/// <param name="TrailPath">Its breadcrumb trail's current file, or null when the trail was off.</param>
public sealed record RunMarker(int ProcessId, string ProcessName, DateTimeOffset Started, string Version, string? TrailPath)
{
    /// <summary>The marker's file (set when it was read or written).</summary>
    public string? Path { get; init; }

    /// <summary>When a later start found the run gone (null while it runs).</summary>
    public DateTimeOffset? FoundDead { get; init; }

    /// <summary>The marker as JSON.</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteNumber("pid", ProcessId);
            json.WriteString("process", ProcessName);
            json.WriteString("started", Started.ToString("O", CultureInfo.InvariantCulture));
            json.WriteString("version", Version);
            if (TrailPath is not null)
            {
                json.WriteString("trail", TrailPath);
            }

            if (FoundDead is { } dead)
            {
                json.WriteString("foundDead", dead.ToString("O", CultureInfo.InvariantCulture));
            }

            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The marker in <paramref name="path"/>, or null when it is missing or not a marker.</summary>
    public static RunMarker? Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            DateTimeOffset? dead = root.TryGetProperty("foundDead", out var d) && DateTimeOffset.TryParse(d.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;
            return new RunMarker(root.GetProperty("pid").GetInt32(), root.GetProperty("process").GetString() ?? "",
                DateTimeOffset.Parse(root.GetProperty("started").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "", root.TryGetProperty("trail", out var t) ? t.GetString() : null)
            {
                Path = Path_(path),
                FoundDead = dead,
            };
        }
#pragma warning disable CA1031 // A marker that cannot be read (half written, edited) is not a run.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private static string Path_(string path) => System.IO.Path.GetFullPath(path);
}

/// <summary>
/// The crash watch of a run (the owner's note 21, a crash reporter that needs no hook): at start it looks in the crash
/// folder for the markers of runs whose process is gone (each a run that died, natively or not) and keeps them as
/// <see cref="UncleanRuns"/> (renamed <c>run-&lt;pid&gt;.died.json</c>, with their trails, so the next start does not offer them
/// again), writes this run's marker, starts its <see cref="CrashTrail"/> (<c>CRAFT_CRASH_TRAIL</c>) and records the
/// first-chance exceptions into it. <see cref="EndCleanly"/> on a clean exit removes the marker and the trail. Nothing
/// it does throws: a crash folder that cannot be written leaves the app without a trail, said in <see cref="Problem"/>.
/// </summary>
public sealed class CrashWatch : IDisposable
{
    /// <summary>The marker files' prefix.</summary>
    public const string MarkerPrefix = "run-";

    /// <summary>The ending of a marker found dead.</summary>
    public const string DiedSuffix = ".died.json";

    private readonly EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs>? _firstChance;
    private bool _ended;

    private CrashWatch(string folder, RunMarker current, IReadOnlyList<RunMarker> unclean, CrashTrail? trail, string? problem, bool watchExceptions = true)
    {
        Folder = folder;
        Current = current;
        UncleanRuns = unclean;
        Trail = trail;
        Problem = problem;
        if (trail is not null && watchExceptions)
        {
            _firstChance = (_, e) => trail.Exception(e.Exception);
            AppDomain.CurrentDomain.FirstChanceException += _firstChance;
        }
    }

    /// <summary>The crash folder (<see cref="CrashReports.Folder"/>).</summary>
    public string Folder { get; }

    /// <summary>This run's marker.</summary>
    public RunMarker Current { get; }

    /// <summary>The runs that died since the last start found them (the ticket is offered for these), the newest first.</summary>
    public IReadOnlyList<RunMarker> UncleanRuns { get; }

    /// <summary>This run's breadcrumb trail, or null when it is off (<c>CRAFT_CRASH_TRAIL</c>) or could not start.</summary>
    public CrashTrail? Trail { get; }

    /// <summary>Why the marker or the trail could not be written, or null.</summary>
    public string? Problem { get; }

    /// <summary>The trail being recorded now (the crash dialog flushes it), or null.</summary>
    public static CrashTrail? CurrentTrail { get; private set; }

    /// <summary>
    /// Starts the watch of this run in <paramref name="options"/>' crash folder. <paramref name="isAlive"/> tells whether a
    /// marker's process still runs (the system's processes when null: the id with the same name and start time);
    /// <paramref name="processId"/>, <paramref name="processName"/> and <paramref name="now"/> stand for this process's (tests).
    /// </summary>
    public static CrashWatch Begin(CraftOptions options, string? version = null, Func<RunMarker, bool>? isAlive = null, int? processId = null,
        string? processName = null, DateTimeOffset? now = null, bool watchExceptions = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        var folder = CrashReports.Folder(options);
        var started = now ?? DateTimeOffset.Now;
        int pid = processId ?? Environment.ProcessId;
        string name = processName ?? CurrentProcessName();
        version ??= AppVersion();
        isAlive ??= IsAlive;
        var unclean = new List<RunMarker>();
        string? problem = null;
        CrashTrail? trail = null;
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var file in Directory.EnumerateFiles(folder, MarkerPrefix + "*.json"))
            {
                if (file.EndsWith(DiedSuffix, StringComparison.OrdinalIgnoreCase) || RunMarker.Read(file) is not { } marker)
                {
                    continue;
                }

                if (marker.ProcessId == pid || isAlive(marker))
                {
                    continue;                                        // another window of Craft running now, or this process
                }

                // Its trail is renamed with its start, so a later process given the same id cannot take it over.
                string? keptTrail = marker.TrailPath;
                if (keptTrail is not null && File.Exists(keptTrail))
                {
                    keptTrail = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"trail-{marker.ProcessId}-{marker.Started.LocalDateTime:yyyyMMdd-HHmmss}.txt"));
                    File.Move(marker.TrailPath!, keptTrail, overwrite: true);
                    if (File.Exists(CrashTrail.OlderOf(marker.TrailPath!)))
                    {
                        File.Move(CrashTrail.OlderOf(marker.TrailPath!), CrashTrail.OlderOf(keptTrail), overwrite: true);
                    }
                }

                var died = marker with
                {
                    FoundDead = started,
                    TrailPath = keptTrail,
                    Path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{MarkerPrefix}{marker.ProcessId}-{marker.Started.LocalDateTime:yyyyMMdd-HHmmss}{DiedSuffix}")),
                };
                File.WriteAllText(died.Path, died.ToJson());
                File.Delete(file);
                unclean.Add(died);
            }

            unclean.Sort(static (a, b) => b.Started.CompareTo(a.Started));
            Prune(folder, options.CrashRunsKept);
            string? trailPath = options.CrashTrail ? Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"trail-{pid}.txt")) : null;
            if (trailPath is not null)
            {
                // A trail of an earlier process of the same id (ids are reused) is not this run's.
                File.Delete(trailPath);
                File.Delete(CrashTrail.OlderOf(trailPath));
                trail = new CrashTrail(trailPath, options.CrashTrailKilobytes, watchExceptions ? options.CrashTrailExceptionsPerSecond : 0);
                trail.Note(string.Create(CultureInfo.InvariantCulture, $"Craft {version} started (process {pid}, {name})"));
            }

            var current = new RunMarker(pid, name, started, version, trailPath) { Path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{MarkerPrefix}{pid}.json")) };
            File.WriteAllText(current.Path, current.ToJson());
            var watch = new CrashWatch(folder, current, unclean, trail, null, watchExceptions);
            CurrentTrail = trail;
            return watch;
        }
#pragma warning disable CA1031 // The watch never stops the app: what failed is said.
        catch (Exception error)
#pragma warning restore CA1031
        {
            problem = $"The crash folder {folder} could not be written ({error.GetType().Name}: {error.Message}): no run marker and no trail this run.";
            trail?.Dispose();
            return new CrashWatch(folder, new RunMarker(pid, name, started, version, null), unclean, null, problem);
        }
    }

    /// <summary>The markers of the runs that died and were kept (<c>run-&lt;pid&gt;.died.json</c>), the newest first.</summary>
    public IReadOnlyList<RunMarker> DiedRuns()
    {
        var list = new List<RunMarker>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(Folder, MarkerPrefix + "*" + DiedSuffix))
            {
                if (RunMarker.Read(file) is { } marker)
                {
                    list.Add(marker);
                }
            }
        }
#pragma warning disable CA1031 // A folder that cannot be listed has no runs to show.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        list.Sort(static (a, b) => b.Started.CompareTo(a.Started));
        return list;
    }

    /// <summary>The clean exit: the trail closed, this run's marker and trail removed (the next start finds nothing).</summary>
    public void EndCleanly()
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        Unhook();
        Trail?.Dispose();
        TryDelete(Current.Path);
        if (Current.TrailPath is { } trail)
        {
            TryDelete(trail);
            TryDelete(CrashTrail.OlderOf(trail));
        }
    }

    /// <summary>Stops recording without the clean exit's removal (the marker stays: the next start reports this run).</summary>
    public void Dispose()
    {
        Unhook();
        if (!_ended)
        {
            Trail?.Dispose();
        }
    }

    /// <summary>Whether <paramref name="marker"/>'s process runs now: the same id, name and start time (within 2 s).</summary>
    public static bool IsAlive(RunMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        try
        {
            using var process = Process.GetProcessById(marker.ProcessId);
            if (!string.Equals(process.ProcessName, marker.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                return Math.Abs((process.StartTime - marker.Started.LocalDateTime).TotalSeconds) < 2;
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                return true;                                         // its start time is not readable: the same name counts
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return false;                                            // no process of that id
        }
    }

    /// <summary>The app's version: the entry assembly's informational version, else its version.</summary>
    public static string AppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(CrashWatch).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static string CurrentProcessName()
    {
        using var process = Process.GetCurrentProcess();
        return process.ProcessName;
    }

    // Keeps the newest <keep> runs that died (their markers and trails); older ones go.
    private static void Prune(string folder, int keep)
    {
        var died = Directory.EnumerateFiles(folder, MarkerPrefix + "*" + DiedSuffix).Select(f => (File: f, Marker: RunMarker.Read(f)))
            .OrderByDescending(r => r.Marker?.Started ?? DateTimeOffset.MinValue).ToList();
        foreach (var (file, marker) in died.Skip(Math.Max(0, keep)))
        {
            TryDelete(file);
            if (marker?.TrailPath is { } trail)
            {
                TryDelete(trail);
                TryDelete(CrashTrail.OlderOf(trail));
            }
        }
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Unhook()
    {
        if (_firstChance is not null)
        {
            AppDomain.CurrentDomain.FirstChanceException -= _firstChance;
        }

        if (ReferenceEquals(CurrentTrail, Trail))
        {
            CurrentTrail = null;
        }
    }
}
