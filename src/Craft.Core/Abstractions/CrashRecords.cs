// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Core.Abstractions;

/// <summary>Which run's crash the platform is asked about.</summary>
/// <param name="ProcessId">The run's process id.</param>
/// <param name="ProcessName">Its process name (Craft.App), for records that carry no id.</param>
/// <param name="From">When the run started.</param>
/// <param name="To">When it was found gone (or now).</param>
public sealed record CrashRecordQuery(int ProcessId, string ProcessName, DateTimeOffset From, DateTimeOffset To);

/// <summary>What the platform recorded of a crash (Windows: an Application Error 1000, a .NET Runtime 1026).</summary>
/// <param name="Time">When it was recorded.</param>
/// <param name="Source">Where it was read ("Application log, Application Error 1000").</param>
/// <param name="Summary">One line: the faulting module and exception code, or the managed exception.</param>
/// <param name="Text">The record as the platform holds it (the module, code, offset, path; the managed stack).</param>
public sealed record CrashRecord(DateTimeOffset Time, string Source, string Summary, string Text);

/// <summary>
/// Reads the platform's own record of a run that died (a native fail-fast leaves no managed report), registered in
/// <see cref="CrashRecordSources"/>: Windows' Application event log on Windows (Craft.Maui's Platforms/Windows); another
/// platform registers its own. It only reads, and runs off the UI thread (the problem report gathers it).
/// </summary>
public interface ICrashRecordSource : IKind
{
    /// <summary>Why it cannot read here, or null when it can.</summary>
    string? Unavailable { get; }

    /// <summary>The records of <paramref name="query"/>'s run, the oldest first (none when the platform kept none).</summary>
    IReadOnlyList<CrashRecord> Read(CrashRecordQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// The crash record sources, by name (ignoring case). The library's: <see cref="None"/> (reads nothing). A platform
/// registers its own (Windows: "windows-eventlog"); <c>CRAFT_CRASH_RECORDS</c> picks one, "auto" the first registered
/// one that can read here. An unknown name fails with "not registered".
/// </summary>
public static class CrashRecordSources
{
    /// <summary>The source that reads nothing.</summary>
    public const string None = "none";

    /// <summary>The option's value for the first registered source that can read here.</summary>
    public const string Auto = "auto";

    private static readonly KindTable<ICrashRecordSource> Table = BuiltIn();

    /// <summary>Registers <paramref name="source"/> under its name (over the library's of that name).</summary>
    public static void Register(ICrashRecordSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Table.Register(source, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the registration of that name (the library's, if any, answers again).</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The source of that name, or null.</summary>
    public static ICrashRecordSource? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The source of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static ICrashRecordSource Get(ReadOnlySpan<char> name) => Table.Get(name);

    /// <summary>The source <paramref name="name"/> names: <see cref="Auto"/> is the first registered one that can read here, else <see cref="None"/>.</summary>
    /// <exception cref="NotRegisteredException">A name that is not registered.</exception>
    public static ICrashRecordSource Resolve(string? name)
    {
        if (name is { Length: > 0 } && !name.Equals(Auto, StringComparison.OrdinalIgnoreCase))
        {
            return Get(name);
        }

        foreach (var source in Table.Kinds)
        {
            if (!source.Name.Equals(None, StringComparison.OrdinalIgnoreCase) && source.Unavailable is null)
            {
                return source;
            }
        }

        return Get(None);
    }

    private static KindTable<ICrashRecordSource> BuiltIn()
    {
        var table = new KindTable<ICrashRecordSource>(nameof(CrashRecordSources), "Crash record source", "add it with CrashRecordSources.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a source is read once, when a problem report is written");
        table.RegisterDefault(new NoCrashRecords());
        return table;
    }

    private sealed class NoCrashRecords : ICrashRecordSource
    {
        public string Name => None;

        public string Summary => "reads nothing (this platform's record of a crash is not read)";

        public string? Unavailable => null;

        public IReadOnlyList<CrashRecord> Read(CrashRecordQuery query, CancellationToken cancellationToken = default) => [];
    }
}
