// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;
using Craft.Core.Abstractions;
using Craft.Core.Diagnostics;
using Craft.Editor.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>
/// "Report a problem" (the owner's note 21): offered at the start after a run that did not close cleanly
/// (<c>CRAFT_CRASH_TICKET_OFFER</c>) and always from Help › Report a problem. It gathers the run's marker, the
/// platform's record of the crash (<see cref="CrashRecordSources"/>, <c>CRAFT_CRASH_RECORDS</c>), the run's crash and
/// stall reports, its breadcrumb trail, the app's and the system's details and the plug-ins, off the UI thread as an
/// activity, writes one <c>ticket-&lt;time&gt;.txt</c> in the crash folder (<see cref="CrashTicket"/>) and shows it for review
/// with Copy, Open folder and Open the report. Nothing is sent anywhere. Used on the UI thread.
/// </summary>
public sealed class ProblemReport : IDisposable
{
    /// <summary>The command: Help › Report a problem.</summary>
    public const string CommandId = "craft.reportProblem";

    private readonly EditorHost _host;
    private readonly CrashWatch? _watch;
    private readonly IReadOnlyList<TicketPlugin> _plugins;
    private readonly IEditorClipboard _clipboard;
    private readonly Func<string, Task>? _openFolder;
    private readonly Func<string, Task>? _openFile;
    private bool _disposed;

    /// <summary>The report of <paramref name="watch"/>'s runs (none when the watch did not start).</summary>
    /// <param name="host">The host (its dialogs, activities, log and settings).</param>
    /// <param name="watch">The run's crash watch, or null.</param>
    /// <param name="plugins">The plug-ins as they loaded.</param>
    /// <param name="clipboard">The clipboard (Copy).</param>
    /// <param name="openFolder">Opens a folder in the system's file manager (Open folder), or null.</param>
    /// <param name="openFile">Opens a file in an editor tab (Open the report), or null.</param>
    public ProblemReport(EditorHost host, CrashWatch? watch, IReadOnlyList<TicketPlugin> plugins, IEditorClipboard clipboard,
        Func<string, Task>? openFolder = null, Func<string, Task>? openFile = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(clipboard);
        _host = host;
        _watch = watch;
        _plugins = plugins;
        _clipboard = clipboard;
        _openFolder = openFolder;
        _openFile = openFile;
        EditorCommands.Register(new EditorCommand(CommandId, "Help: Report a problem", (_, _) => ReportAsync(),
            summary: "Writes a problem report (the last crash's or this run's) to the crash folder, for you to review and paste; nothing is sent"));
        EditorMenus.Register(new MenuItem(EditorMenus.Help, CommandId, "Report a problem", 0, 9));
    }

    /// <summary>The last report written this session, or null.</summary>
    public string? LastPath { get; private set; }

    /// <summary>
    /// At start: says why the watch could not start, if so, and when a run died since the last start (and
    /// <c>CRAFT_CRASH_TICKET_OFFER</c> is on) asks whether to write its report.
    /// </summary>
    public async Task OfferAsync()
    {
        if (_watch is null)
        {
            return;
        }

        if (_watch.Problem is { } problem)
        {
            _host.Log(EditorLogLevel.Warning, problem);
        }

        if (_watch.UncleanRuns is not [var run, ..])
        {
            return;
        }

        _host.Log(EditorLogLevel.Warning, string.Create(CultureInfo.InvariantCulture,
            $"The last run (process {run.ProcessId}, started {run.Started:yyyy-MM-dd HH:mm:ss}) did not close cleanly. Help › Report a problem writes its report."));
        if (!_host.Options.CrashTicketOffer)
        {
            return;
        }

        int answer = await _host.AskAsync(new EditorQuestion("Craft did not close cleanly last time",
            "The last run ended without closing (a crash, or it was ended from outside). A problem report gathers what it did last, Windows' record of the crash and the app's details into one text file in the crash folder, for you to read and paste. Nothing is sent anywhere.",
            ["Write a report", "Not now"])
        {
            Details = [string.Create(CultureInfo.InvariantCulture, $"Run: process {run.ProcessId}, Craft {run.Version}, started {run.Started:yyyy-MM-dd HH:mm:ss}"), "Folder: " + _watch.Folder],
            Dismissed = 1,
        });
        if (answer == 0)
        {
            await WriteAndReviewAsync(run, died: true);
        }
    }

    /// <summary>Help › Report a problem: the run that died last (asked first, when there is one) or this run; written and shown.</summary>
    public async Task ReportAsync()
    {
        if (_watch is null)
        {
            await _host.ShowMessageAsync("Craft: Report a problem", "The crash watch did not start this run, so there is nothing to report from.");
            return;
        }

        var died = _watch.DiedRuns();
        if (died is [var last, ..])
        {
            int answer = await _host.AskAsync(new EditorQuestion("Report a problem", "Which run is the report about?",
                [string.Create(CultureInfo.InvariantCulture, $"The run that did not close cleanly, started {last.Started:yyyy-MM-dd HH:mm:ss}"), "This run"]));
            if (answer < 0)
            {
                return;
            }

            await WriteAndReviewAsync(answer == 0 ? last : _watch.Current, died: answer == 0);
            return;
        }

        await WriteAndReviewAsync(_watch.Current, died: false);
    }

    /// <summary>
    /// Writes the report of <paramref name="run"/> (<paramref name="died"/>: a run that died, else this one) off the UI
    /// thread as an activity; its path and text, or null when it could not be written (said in Output and the activity).
    /// </summary>
    public async Task<(string Path, string Text)?> WriteAsync(RunMarker run, bool died, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (_watch is null)
        {
            return null;
        }

        using var activity = _host.Activities.Start("Writing a problem report", ActivityKinds.General, cancellable: true, cancellationToken);
        var options = _host.Options;
        var folder = _watch.Folder;
        var current = _watch.Current;
        try
        {
            if (current.TrailPath is not null && !died && _watch.Trail is { } trail)
            {
                await trail.FlushAsync().WaitAsync(TimeSpan.FromSeconds(2), activity.CancellationToken);
            }

            activity.Report(null, "reading the platform's record, the reports and the trail");
            var source = CrashRecordSources.Resolve(options.CrashRecords);
            var token = activity.CancellationToken;
            var plugins = _plugins;
            var (path, text) = await Task.Run(() =>
            {
                var content = CrashTicket.Gather(folder, run, died, source, plugins, options.CrashTicketTrailLines, died ? current : null, cancellationToken: token);
                var text = CrashTicket.Format(content);
                return (CrashTicket.Write(folder, text), text);
            }, token);
            LastPath = path;
            _watch.Trail?.Note("A problem report was written: " + Path.GetFileName(path));
            _host.Log(EditorLogLevel.Information, "A problem report was written: " + path);
            activity.Complete(Path.GetFileName(path));
            return (path, text);
        }
        catch (OperationCanceledException)
        {
            activity.Complete("cancelled");
            return null;
        }
#pragma warning disable CA1031 // A report that cannot be written is said; the app goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Log(EditorLogLevel.Error, $"The problem report could not be written to {folder}: {error.Message}", error);
            activity.Fail($"The problem report could not be written: {error.Message}", error);
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        EditorMenus.Unregister(EditorMenus.Help, CommandId);
        EditorCommands.Unregister(CommandId);
    }

    private async Task WriteAndReviewAsync(RunMarker run, bool died)
    {
        if (await WriteAsync(run, died) is not { } written)
        {
            return;
        }

        var (path, text) = written;

        // The review: Copy, Open folder and Open the report leave it open; Close (or Escape) ends it.
        while (!_disposed)
        {
            int answer = await _host.AskAsync(new EditorQuestion("Problem report written",
                "It is a text file in the crash folder, for you to read and paste where you report the problem. Nothing was sent anywhere; your user name and e-mail are not in it.",
                ["Copy", "Open folder", "Open the report", "Close"])
            {
                Details = [path, string.Create(CultureInfo.InvariantCulture, $"{text.Length / 1024.0:F1} KiB, {text.AsSpan().Count('\n')} lines")],
                Dismissed = 3,
            });
            switch (answer)
            {
                case 0:
                    try
                    {
                        await _clipboard.SetTextAsync(text);
                        _host.Log(EditorLogLevel.Information, "The problem report is on the clipboard.");
                    }
#pragma warning disable CA1031 // A copy that fails is said; the review goes on.
                    catch (Exception error)
#pragma warning restore CA1031
                    {
                        _host.Log(EditorLogLevel.Warning, $"The problem report could not be copied: {error.Message}", error);
                    }

                    break;
                case 1 when _openFolder is not null:
                    await Open(_openFolder, Path.GetDirectoryName(path)!);
                    break;
                case 2 when _openFile is not null:
                    await Open(_openFile, path);
                    return;                                          // the report is in a tab now
                default:
                    return;
            }
        }
    }

    private async Task Open(Func<string, Task> open, string path)
    {
        try
        {
            await open(path);
        }
#pragma warning disable CA1031 // A folder or file that does not open is said; the review goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _host.Log(EditorLogLevel.Warning, $"{path} did not open: {error.Message}", error);
        }
    }
}
