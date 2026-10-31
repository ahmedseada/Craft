// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>How low memory runs.</summary>
public enum MemoryPressureLevel
{
    /// <summary>Memory is running high: free what can be built again.</summary>
    High,

    /// <summary>The platform is about to end apps for memory: free everything that can be freed.</summary>
    Critical,
}

/// <summary>
/// Tells the host when memory runs low, registered in <see cref="MemoryPressureMonitors"/>. The library's "gc" reads the
/// memory load the runtime's garbage collector measures (the system's, or a container's limit) after each full collection,
/// on every platform; a platform may register one of its own (an app's memory-usage notifications) under another name.
/// </summary>
public interface IMemoryPressureMonitor : IKind
{
    /// <summary>
    /// Starts watching: <paramref name="onPressure"/> is called on the thread pool (never on the UI thread, and never twice
    /// at once) each time memory runs low. Dispose the result to stop.
    /// </summary>
    IDisposable Watch(Action<MemoryPressureLevel> onPressure);
}

/// <summary>
/// The memory-pressure monitors, by name (ignoring case). The library's: <see cref="Gc"/>. An unknown name fails with "not
/// registered"; "none" (<see cref="None"/>) watches nothing.
/// </summary>
public static class MemoryPressureMonitors
{
    /// <summary>The library's monitor: the garbage collector's memory load against its high-load threshold.</summary>
    public const string Gc = "gc";

    /// <summary>A monitor that never reports (memory is freed only when the user or a command asks).</summary>
    public const string None = "none";

    private static readonly KindTable<IMemoryPressureMonitor> Table = BuiltIn();

    private static KindTable<IMemoryPressureMonitor> BuiltIn()
    {
        var table = new KindTable<IMemoryPressureMonitor>(nameof(MemoryPressureMonitors), "Memory-pressure monitor", "add it with MemoryPressureMonitors.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a monitor is asked once, when the host starts watching");
        table.RegisterDefault(new GcMemoryPressureMonitor());
        table.RegisterDefault(new NoMemoryPressureMonitor());
        return table;
    }

    /// <summary>Registers <paramref name="monitor"/> under its name (over the library's of that name, which stays behind it).</summary>
    public static void Register(IMemoryPressureMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        Table.Register(monitor, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's monitor of that name (the library's, if any, answers again).</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The monitor of that name, or null.</summary>
    public static IMemoryPressureMonitor? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The monitor of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IMemoryPressureMonitor Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>Asks every registered language to free what it can build again (<see cref="ILanguage.ReleaseMemory"/>).</summary>
    public static void ReleaseLanguages()
    {
        foreach (var language in EditorLanguages.All)
        {
            language.ReleaseMemory();
        }
    }
}

/// <summary>
/// The library's monitor: after each full (generation 2) collection, the memory load the collector measured against its
/// high-load threshold (<see cref="GCMemoryInfo.HighMemoryLoadThresholdBytes"/>: by default 90% of the system's memory,
/// or of a container's limit, as the runtime's <c>GCHighMemoryPercent</c> sets it). High at the threshold, critical
/// half-way from it to all of it. No thread or timer runs: an object the collector finalizes after each full collection
/// checks the load and hands a report to the thread pool.
/// </summary>
internal sealed class GcMemoryPressureMonitor : IMemoryPressureMonitor
{
    public string Name => MemoryPressureMonitors.Gc;

    public string Summary => "the garbage collector's memory load against its high-load threshold, after each full collection";

    public IDisposable Watch(Action<MemoryPressureLevel> onPressure)
    {
        ArgumentNullException.ThrowIfNull(onPressure);
        var watch = new Watcher(onPressure);
        _ = new Sentinel(watch);
        return watch;
    }

    /// <summary>The level a collection's figures mean, or null for none.</summary>
    internal static MemoryPressureLevel? LevelOf(long load, long threshold, long total)
    {
        if (threshold <= 0 || load < threshold)
        {
            return null;
        }

        return total > threshold && load >= threshold + ((total - threshold) / 2) ? MemoryPressureLevel.Critical : MemoryPressureLevel.High;
    }

    private sealed class Watcher(Action<MemoryPressureLevel> onPressure) : IDisposable
    {
        private int _running;

        public volatile bool Stopped;

        public long LastCollection = -1;

        public void Report(MemoryPressureLevel level)
        {
            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                return;
            }

            ThreadPool.UnsafeQueueUserWorkItem(static state =>
            {
                var (watcher, level) = state;
                try
                {
                    if (!watcher.Stopped)
                    {
                        watcher._onPressure(level);
                    }
                }
                finally
                {
                    Volatile.Write(ref watcher._running, 0);
                }
            }, (this, level), preferLocal: false);
        }

        private readonly Action<MemoryPressureLevel> _onPressure = onPressure;

        public void Dispose() => Stopped = true;
    }

    /// <summary>Finalized after each collection that reaches it (it lives in generation 2 once promoted); re-registers itself.</summary>
    private sealed class Sentinel(Watcher watcher)
    {
        ~Sentinel()
        {
            if (watcher.Stopped || Environment.HasShutdownStarted)
            {
                return;
            }

            var info = GC.GetGCMemoryInfo(GCKind.FullBlocking);
            if (info.Index != watcher.LastCollection)
            {
                watcher.LastCollection = info.Index;
                if (LevelOf(info.MemoryLoadBytes, info.HighMemoryLoadThresholdBytes, info.TotalAvailableMemoryBytes) is { } level)
                {
                    watcher.Report(level);
                }
            }

            GC.ReRegisterForFinalize(this);
        }
    }
}

/// <summary>"none": watches nothing.</summary>
internal sealed class NoMemoryPressureMonitor : IMemoryPressureMonitor
{
    public string Name => MemoryPressureMonitors.None;

    public string Summary => "never reports: memory is freed only when asked";

    public IDisposable Watch(Action<MemoryPressureLevel> onPressure)
    {
        ArgumentNullException.ThrowIfNull(onPressure);
        return Stop.Instance;
    }

    private sealed class Stop : IDisposable
    {
        public static readonly Stop Instance = new();

        public void Dispose()
        {
        }
    }
}
