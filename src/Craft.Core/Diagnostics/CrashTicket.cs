// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Craft.Core.Abstractions;
using Craft.Core.Plugins;

namespace Craft.Core.Diagnostics;

/// <summary>A plug-in as the problem report lists it.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Version">Its version, or null.</param>
/// <param name="State">loaded, disabled, outdated, or why it did not load.</param>
public sealed record TicketPlugin(string Name, string? Version, string State)
{
    /// <summary>The report's rows of <paramref name="plugins"/>.</summary>
    public static IReadOnlyList<TicketPlugin> Of(IEnumerable<PluginLoadResult> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        return [.. plugins.Select(p => new TicketPlugin(p.Name, p.Version,
            p.Disabled ? "disabled" : !p.Loaded ? "did not load: " + p.Error : p.Outdated ? "loaded (built against an older contract)" : "loaded"))];
    }
}

/// <summary>What a problem report is made of (<see cref="CrashTicket.Gather"/> reads it; <see cref="CrashTicket.Format"/> writes it).</summary>
public sealed record TicketContent
{
    /// <summary>The run it is about.</summary>
    public required RunMarker Run { get; init; }

    /// <summary>Whether the run died (else it is the run asking, from Help).</summary>
    public bool Died { get; init; }

    /// <summary>The run that asked for the report, when it is about an earlier one.</summary>
    public RunMarker? AskedFrom { get; init; }

    /// <summary>The crash record source read (its name), and why it could not read, if so.</summary>
    public string RecordSource { get; init; } = CrashRecordSources.None;

    /// <summary>Why the platform's records could not be read, or null.</summary>
    public string? RecordProblem { get; init; }

    /// <summary>The platform's records of the crash.</summary>
    public IReadOnlyList<CrashRecord> Records { get; init; } = [];

    /// <summary>The crash and stall reports of the run: each file's name and text.</summary>
    public IReadOnlyList<(string Name, string Text)> Reports { get; init; } = [];

    /// <summary>The trail's last lines, the oldest first.</summary>
    public IReadOnlyList<string> Trail { get; init; } = [];

    /// <summary>The plug-ins with their versions.</summary>
    public IReadOnlyList<TicketPlugin> Plugins { get; init; } = [];

    /// <summary>When it was made.</summary>
    public DateTimeOffset Made { get; init; } = DateTimeOffset.Now;
}

/// <summary>
/// The problem report, "an audit or a support ticket" (the owner's note 21): one text file <c>ticket-&lt;time&gt;.txt</c> in
/// the crash folder, made of the run's marker, the platform's record of the crash (Windows' event log), the run's crash
/// and stall reports, its breadcrumb trail, the app's, .NET's and the system's details and the plug-ins with their
/// versions. Nothing is sent anywhere; the user's name, the profile folder, the machine's name and any e-mail address are
/// replaced before it is written (<see cref="Scrub"/>).
/// </summary>
public static class CrashTicket
{
    /// <summary>The ticket files' prefix.</summary>
    public const string Prefix = "ticket-";

    private const int ReportLimit = 32 * 1024;
    private const int ReportsKept = 12;
    private static readonly Regex Email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>
    /// Reads what a report of <paramref name="run"/> holds from <paramref name="folder"/> and <paramref name="source"/>
    /// (file and platform I/O: run it off the UI thread). <paramref name="died"/>: the run died (else it runs now).
    /// </summary>
    public static TicketContent Gather(string folder, RunMarker run, bool died, ICrashRecordSource source, IEnumerable<TicketPlugin> plugins,
        int trailLines, RunMarker? askedFrom = null, DateTimeOffset? now = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(plugins);
        var made = now ?? DateTimeOffset.Now;
        var until = run.FoundDead ?? made;
        IReadOnlyList<CrashRecord> records = [];
        string? recordProblem = source.Unavailable;
        if (died && recordProblem is null)
        {
            try
            {
                records = source.Read(new CrashRecordQuery(run.ProcessId, run.ProcessName, run.Started, until), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // A source that fails is said in the report; the report is still written.
            catch (Exception error)
#pragma warning restore CA1031
            {
                recordProblem = $"{error.GetType().Name}: {error.Message}";
            }
        }

        var reports = new List<(string, string, DateTime)>();
        try
        {
            foreach (var pattern in (ReadOnlySpan<string>)["crash-*.txt", "stall-*.txt", "callback-*.txt"])
            {
                foreach (var file in Directory.EnumerateFiles(folder, pattern))
                {
                    var written = File.GetLastWriteTime(file);
                    if (written < run.Started.LocalDateTime.AddSeconds(-1) || written > until.LocalDateTime.AddSeconds(1))
                    {
                        continue;
                    }

                    var text = File.ReadAllText(file);
                    reports.Add((Path.GetFileName(file), text.Length > ReportLimit ? string.Concat(text.AsSpan(0, ReportLimit), "\n… (cut)\n") : text, written));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            reports.Add(("(the crash folder)", $"could not be read: {error.Message}\n", DateTime.MaxValue));
        }

        reports.Sort(static (a, b) => a.Item3.CompareTo(b.Item3));
        var trail = run.TrailPath is { } path ? CrashTrail.Read(path, trailLines) : [];
        return new TicketContent
        {
            Run = run,
            Died = died,
            AskedFrom = askedFrom,
            RecordSource = source.Name,
            RecordProblem = recordProblem,
            Records = records,
            Reports = [.. reports.TakeLast(ReportsKept).Select(r => (r.Item1, r.Item2))],
            Trail = trail,
            Plugins = [.. plugins],
            Made = made,
        };
    }

    /// <summary>The report's text (scrubbed).</summary>
    public static string Format(TicketContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var run = content.Run;
        var text = new StringBuilder(16 * 1024);
        text.Append(CultureInfo.InvariantCulture, $"Craft problem report, {content.Made:yyyy-MM-dd HH:mm:ss zzz}\n");
        text.Append("Written only to this file: nothing in it was sent anywhere. Your user name, profile folder, machine name and any e-mail address are replaced.\n\n");

        text.Append("== The run ==\n");
        text.Append(CultureInfo.InvariantCulture, $"Process {run.ProcessId} ({run.ProcessName}), Craft {run.Version}, started {run.Started:yyyy-MM-dd HH:mm:ss zzz}\n");
        text.Append(content.Died
            ? string.Create(CultureInfo.InvariantCulture, $"It did not close cleanly: its marker was still there when Craft started again at {run.FoundDead ?? content.Made:yyyy-MM-dd HH:mm:ss zzz}.\n")
            : "It is the run that wrote this report (Help › Report a problem); it is still running.\n");
        if (content.AskedFrom is { } asked)
        {
            text.Append(CultureInfo.InvariantCulture, $"Asked from process {asked.ProcessId}, started {asked.Started:yyyy-MM-dd HH:mm:ss zzz}.\n");
        }

        text.Append("\n== The platform's record of the crash ==\n");
        if (!content.Died)
        {
            text.Append("(not asked: the run is still running)\n");
        }
        else if (content.RecordProblem is { } problem)
        {
            text.Append(CultureInfo.InvariantCulture, $"Source {content.RecordSource}: could not read ({problem}).\n");
        }
        else if (content.Records.Count == 0)
        {
            text.Append(content.RecordSource.Equals(CrashRecordSources.None, StringComparison.OrdinalIgnoreCase)
                ? "No crash record source on this platform (CRAFT_CRASH_RECORDS).\n"
                : $"Source {content.RecordSource}: no record of this process (it may have been ended from outside, as by Task Manager, which leaves none).\n");
        }
        else
        {
            foreach (var record in content.Records)
            {
                text.Append(CultureInfo.InvariantCulture, $"{record.Time.ToLocalTime():yyyy-MM-dd HH:mm:ss}  {record.Source}\n  {record.Summary}\n");
                foreach (var line in record.Text.Split('\n'))
                {
                    if (line.Length > 0)
                    {
                        text.Append("    ").Append(line).Append('\n');
                    }
                }
            }
        }

        text.Append("\n== Crash and stall reports of the run ==\n");
        if (content.Reports.Count == 0)
        {
            text.Append("(none: no managed exception reached a handler and no stall was reported)\n");
        }

        foreach (var (name, body) in content.Reports)
        {
            text.Append("--- ").Append(name).Append(" ---\n").Append(body);
            if (!body.EndsWith('\n'))
            {
                text.Append('\n');
            }
        }

        text.Append(CultureInfo.InvariantCulture, $"\n== The last things it did (the breadcrumb trail, the oldest first; {content.Trail.Count} lines) ==\n");
        if (content.Trail.Count == 0)
        {
            text.Append(run.TrailPath is null ? "(the trail was off: CRAFT_CRASH_TRAIL)\n" : "(the trail holds nothing)\n");
        }

        foreach (var line in content.Trail)
        {
            text.Append(line).Append('\n');
        }

        text.Append("\n== App and system ==\n");
        text.Append(CultureInfo.InvariantCulture, $"Craft {CrashWatch.AppVersion()}; {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}); process {RuntimeInformation.ProcessArchitecture}\n");
        text.Append(CultureInfo.InvariantCulture, $"{Environment.ProcessorCount} logical processors; {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)} MiB of memory available to the process; culture {CultureInfo.CurrentCulture.Name}\n");

        text.Append("\n== Plug-ins ==\n");
        if (content.Plugins.Count == 0)
        {
            text.Append("(none)\n");
        }

        foreach (var plugin in content.Plugins)
        {
            text.Append(CultureInfo.InvariantCulture, $"{plugin.Name} {plugin.Version ?? "(no version)"}: {plugin.State}\n");
        }

        return Scrub(text.ToString());
    }

    /// <summary>Writes <paramref name="text"/> as <c>ticket-&lt;time&gt;.txt</c> in <paramref name="folder"/>; its path.</summary>
    /// <exception cref="IOException">The folder cannot be written.</exception>
    public static string Write(string folder, string text, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(text);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{Prefix}{(now ?? DateTimeOffset.Now).LocalDateTime:yyyyMMdd-HHmmss}.txt"));
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>
    /// <paramref name="text"/> with the user's profile folder, user name (of three letters or more, as a whole word),
    /// machine name and any e-mail address replaced (<paramref name="userName"/>, <paramref name="profile"/> and
    /// <paramref name="machine"/> default to this system's).
    /// </summary>
    public static string Scrub(string text, string? userName = null, string? profile = null, string? machine = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        userName ??= Environment.UserName;
        profile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        machine ??= Environment.MachineName;
        if (profile is { Length: > 3 })
        {
            text = text.Replace(profile, "<profile>", StringComparison.OrdinalIgnoreCase);
            text = text.Replace(profile.Replace('\\', '/'), "<profile>", StringComparison.OrdinalIgnoreCase);
        }

        text = Email.Replace(text, "<e-mail>");
        foreach (var (word, replacement) in (ReadOnlySpan<(string?, string)>)[(userName, "<user>"), (machine, "<machine>")])
        {
            if (word is { Length: >= 3 })
            {
                text = Regex.Replace(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(word)}(?![\p{{L}}\p{{N}}])", replacement,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            }
        }

        return text;
    }
}
