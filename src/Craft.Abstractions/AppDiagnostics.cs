// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;

namespace Craft.Abstractions;

/// <summary>
/// The editor's MCP server as a plug-in reaches it (the Claude Code plug-in connects an outside agent to it), registered
/// in <see cref="EditorMcpServers"/> by the plug-in that runs it (the AI plug-in's "craft"): where it listens while it
/// runs, its bearer token, and the command that starts it. Read on the UI thread.
/// </summary>
public interface IEditorMcpServer : IKind
{
    /// <summary>Where clients connect (<c>http://127.0.0.1:PORT/mcp</c>) while it runs, else null.</summary>
    string? Address { get; }

    /// <summary>The bearer token clients send while it runs, or null (off, or no token required).</summary>
    string? Token { get; }

    /// <summary>The command that starts it (in <see cref="EditorCommands"/>), or "" when it cannot be started from a command.</summary>
    string StartCommand { get; }

    /// <summary>Raised on the UI thread when it started or stopped.</summary>
    event EventHandler? Changed;
}

/// <summary>The editor's MCP servers, by name (ignoring case). The AI plug-in registers <see cref="Editor"/>; an unknown name fails with "not registered".</summary>
public static class EditorMcpServers
{
    /// <summary>The editor's own server (its live tools: the AI plug-in's).</summary>
    public const string Editor = "craft";

    private static readonly KindTable<IEditorMcpServer> Table = new(nameof(EditorMcpServers), "Editor MCP server", "add it with EditorMcpServers.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a server's address and token are read when a client is connected");

    /// <summary>Registers <paramref name="server"/> under its name.</summary>
    public static void Register(IEditorMcpServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        Table.Register(server, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the server of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The server of that name, or null.</summary>
    public static IEditorMcpServer? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The server of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IEditorMcpServer Get(ReadOnlySpan<char> name) => Table.Get(name);
}

/// <summary>The kinds of the reports the app writes to its crash folder (<see cref="IEditorDiagnostics.ReportFolder"/>), by their file names' prefixes.</summary>
public static class DiagnosticReportKinds
{
    /// <summary>An exception that ended a run (<c>crash-*.txt</c>).</summary>
    public const string Crash = "crash";

    /// <summary>A platform callback's failure, caught: the app went on (<c>callback-*.txt</c>).</summary>
    public const string Callback = "callback";

    /// <summary>A stall of the UI thread the watchdog saw (<c>stall-*.txt</c>).</summary>
    public const string Stall = "stall";

    /// <summary>Every kind, in order.</summary>
    public static IReadOnlyList<string> All { get; } = [Crash, Callback, Stall];

    /// <summary>The kind of the report file named <paramref name="fileName"/> ("crash-20261101-101500-123.txt"), or null when it is none.</summary>
    public static string? Of(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        foreach (var kind in All)
        {
            if (fileName.StartsWith(kind + "-", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return null;
    }
}

/// <summary>A report the app wrote (a crash, a caught callback failure, a stall).</summary>
/// <param name="Kind">Its kind (<see cref="DiagnosticReportKinds"/>).</param>
/// <param name="Path">Its file (a full path).</param>
/// <param name="Written">When it was written.</param>
/// <param name="Bytes">Its size.</param>
public sealed record DiagnosticReport(string Kind, string Path, DateTimeOffset Written, long Bytes)
{
    /// <summary>Its file's name ("stall-20261101-101500-123.txt").</summary>
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>The reports of <paramref name="folder"/>, the newest first, of <paramref name="kind"/> (null: every kind), at most <paramref name="max"/>; none when the folder is missing.</summary>
    public static IReadOnlyList<DiagnosticReport> List(string? folder, string? kind, int max)
    {
        if (string.IsNullOrWhiteSpace(folder) || max <= 0 || !Directory.Exists(folder))
        {
            return [];
        }

        var found = new List<DiagnosticReport>();
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.txt"))
            {
                if (DiagnosticReportKinds.Of(file.Name) is { } of && (kind is null || of.Equals(kind, StringComparison.OrdinalIgnoreCase)))
                {
                    found.Add(new DiagnosticReport(of, file.FullName, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero), file.Length));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        found.Sort(static (a, b) => b.Written != a.Written ? b.Written.CompareTo(a.Written) : string.CompareOrdinal(b.Name, a.Name));
        return found.Count <= max ? found : found.GetRange(0, max);
    }

    /// <summary>One line for a listing: "stall · stall-….txt · 2026-11-01 10:15:00 · 4.1 KB".</summary>
    public string Line => string.Create(CultureInfo.InvariantCulture, $"{Kind} · {Name} · {Written.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {Bytes / 1024.0:0.0} KB");
}

/// <summary>A telemetry measure as the app keeps it (its Idrak telemetry table): the last value, the mean, the 95th percentile, how many.</summary>
/// <param name="Source">Who recorded it ("host", a plug-in's name).</param>
/// <param name="Name">The measure ("editor.paint.warm").</param>
/// <param name="Last">Its last value.</param>
/// <param name="Mean">Its mean over the samples kept.</param>
/// <param name="P95">Its 95th percentile over the samples kept.</param>
/// <param name="Count">How many were recorded.</param>
/// <param name="Unit">Its unit ("ms").</param>
public sealed record TelemetryMeasure(string Source, string Name, double Last, double Mean, double P95, long Count, string Unit);

/// <summary>
/// What the app knows about itself (the Claude Code plug-in's diagnostic MCP tools read it): its version, the folder of
/// its reports, the crash trail's last lines, Output's last lines, its telemetry. <see cref="IEditorHost.Diagnostics"/>.
/// <see cref="Reports"/> and <see cref="TrailLines"/> read the disk and are called off the UI thread (any thread);
/// <see cref="OutputLines"/> and <see cref="Measures"/> read what the window keeps and are called on the UI thread. Each
/// is bounded by the caller's count.
/// </summary>
public interface IEditorDiagnostics
{
    /// <summary>The app's version, its runtime and its system ("Craft 0.1.0 · .NET 10.0.14 · Windows 10.0.26100 (X64)").</summary>
    string AppVersion { get; }

    /// <summary>The folder the app writes its reports and its trail to, or null.</summary>
    string? ReportFolder { get; }

    /// <summary>The reports of <paramref name="kind"/> (null: every kind), the newest first, at most <paramref name="max"/>.</summary>
    IReadOnlyList<DiagnosticReport> Reports(string? kind, int max) => DiagnosticReport.List(ReportFolder, kind, max);

    /// <summary>The crash trail's last <paramref name="max"/> lines (the UI work, commands, keys' names, exceptions, Output), oldest first.</summary>
    IReadOnlyList<string> TrailLines(int max);

    /// <summary>Output's last <paramref name="max"/> lines, oldest first.</summary>
    IReadOnlyList<string> OutputLines(int max);

    /// <summary>The telemetry measures, the slowest first, at most <paramref name="max"/>.</summary>
    IReadOnlyList<TelemetryMeasure> Measures(int max);
}

/// <summary>
/// An action offered on a report where the app shows one (the failure dialog "Something failed; Craft kept running", the
/// stall notice): the Claude Code plug-in's "Ask Claude Code". Registered in <see cref="EditorReportActions"/>.
/// </summary>
public interface IEditorReportAction : IKind
{
    /// <summary>Its button's label ("Ask Claude Code").</summary>
    string Label { get; }

    /// <summary>Whether it is offered now (its plug-in is active and can act).</summary>
    bool IsAvailable => true;

    /// <summary>Acts on <paramref name="report"/> (on the UI thread; its work off it).</summary>
    Task RunAsync(DiagnosticReport report, CancellationToken cancellationToken = default);
}

/// <summary>The report actions, by name (ignoring case), in the order registered; the host shows each available one as a button.</summary>
public static class EditorReportActions
{
    private static readonly KindTable<IEditorReportAction> Table = new(nameof(EditorReportActions), "Report action", "add it with EditorReportActions.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "an action runs when the user presses its button");

    /// <summary>Registers <paramref name="action"/> under its name.</summary>
    public static void Register(IEditorReportAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Table.Register(action, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the action of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered actions, in order.</summary>
    public static IReadOnlyList<IEditorReportAction> All => Table.Kinds;

    /// <summary>The action of that name, or null.</summary>
    public static IEditorReportAction? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The report <paramref name="path"/> as a <see cref="DiagnosticReport"/> (its kind from its name, else <paramref name="kind"/>).</summary>
    public static DiagnosticReport ReportOf(string path, string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        bool exists = file.Exists;
        return new DiagnosticReport(DiagnosticReportKinds.Of(file.Name) ?? kind, file.FullName,
            exists ? new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) : DateTimeOffset.Now, exists ? file.Length : 0);
    }
}
