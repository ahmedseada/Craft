// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>
/// A language's workspace in the notification centre (B10): when it finishes loading, one notification ("C# workspace
/// ready: 1 project, 6 MB, 2.3 s", no toast); when it fails, one notification in plain words with what fixes it and a
/// Try again (an error: it toasts). One per workspace and load: a later load of the same workspace (a rebuild after a
/// memory release) replaces its notification in place of adding another, and an open file of the same workspace adds
/// none. The status bar and the explorer no longer hold the "ready" state. Used on the UI thread.
/// </summary>
public sealed class WorkspaceNotices
{
    private readonly Func<string, string, string, NotificationSeverity, IReadOnlyList<NotificationAction>?, IEditorNotification> _post;
    private readonly Dictionary<string, (string Signature, IEditorNotification Notice)> _shown = new(StringComparer.Ordinal);

    /// <summary>Notices posted through <paramref name="post"/> (source, title, text, severity, actions): the centre's <c>Post</c>.</summary>
    public WorkspaceNotices(Func<string, string, string, NotificationSeverity, IReadOnlyList<NotificationAction>?, IEditorNotification> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        _post = post;
    }

    /// <summary>How many notices were posted (the tests' count).</summary>
    public int Posted { get; private set; }

    /// <summary>Looks at <paramref name="model"/>'s semantic layer (the active editor's): posts its workspace's notice once it is ready or failed.</summary>
    public void Observe(EditorModel? model)
    {
        if (model?.SemanticSession is { IsSemanticDeferred: false } session)
        {
            Observe(model.LanguageName, session);
        }
    }

    /// <summary>Looks at <paramref name="session"/> of <paramref name="language"/> ("C#").</summary>
    public void Observe(string language, ISemanticLanguageSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Observe(language, session.Telemetry, () =>
        {
            session.Release();                                        // the next request builds the workspace again
            session.EnableSemantics();
        });
    }

    /// <summary>Looks at a workspace's <paramref name="telemetry"/> of <paramref name="language"/>; <paramref name="retry"/> is a failure's Try again.</summary>
    public void Observe(string language, SemanticTelemetry telemetry, Action retry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(retry);
        string key = language + "|" + (telemetry.WorkspaceRoot ?? "");
        string? signature = telemetry.IsBuilt ? "ready:" + telemetry.Generation.ToString(CultureInfo.InvariantCulture)
            : telemetry.Failure is { } failure ? "failed:" + failure
            : null;
        if (signature is null || (_shown.TryGetValue(key, out var shown) && shown.Signature == signature))
        {
            return;                                                    // loading, or this load was said already
        }

        if (_shown.Remove(key, out var earlier))
        {
            earlier.Notice.Dispose();                                  // a new load of the same workspace takes its place
        }

        string folder = telemetry.WorkspaceRoot is { } root ? Path.GetFileName(Path.TrimEndingDirectorySeparator(root)) : "";
        IEditorNotification notice;
        if (telemetry.IsBuilt)
        {
            notice = _post(language, $"{language} workspace ready", ReadyText(telemetry, folder), NotificationSeverity.Success, null);
        }
        else
        {
            var again = new NotificationAction("Try again", _ =>
            {
                retry();
                return Task.CompletedTask;
            }, Primary: true);
            notice = _post(language, $"{language} workspace failed", Plain(telemetry.Failure!, folder), NotificationSeverity.Error, [again]);
        }

        _shown[key] = (signature, notice);
        Posted++;
    }

    /// <summary>"1 project, 6 MB, 2.3 s (Craft)": the facts of a ready workspace.</summary>
    public static string ReadyText(SemanticTelemetry telemetry, string folder)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        var parts = new List<string>(3)
        {
            string.Create(CultureInfo.InvariantCulture, $"{telemetry.Projects} {(telemetry.Projects == 1 ? "project" : "projects")}"),
            ShellText.Bytes(telemetry.MemoryBytes),
        };
        if (ShellText.Seconds(telemetry.BuildTime) is { Length: > 0 } time)
        {
            parts.Add(time);
        }

        var text = string.Join(", ", parts);
        return folder.Length > 0 ? $"{text} ({folder})" : text;
    }

    /// <summary>A failure in plain words, with what fixes it.</summary>
    public static string Plain(string failure, string folder)
    {
        ArgumentNullException.ThrowIfNull(failure);
        string where = folder.Length > 0 ? $" of {folder}" : "";
        if (failure.Contains("MSBuild", StringComparison.OrdinalIgnoreCase) || failure.Contains("SDK", StringComparison.Ordinal))
        {
            return $"The projects{where} could not be read: the .NET SDK was not found ({failure}). Install the .NET SDK, or choose other workspace readers in Settings, C#; then Try again.";
        }

        if (failure.Contains("Access to the path", StringComparison.OrdinalIgnoreCase) || failure.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            return $"Craft may not read a file{where} ({failure}). Give your account read access to the folder, then Try again.";
        }

        if (failure.Contains("Could not find", StringComparison.OrdinalIgnoreCase) || failure.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return $"A file the projects{where} name is missing ({failure}). Restore or fix the project's reference, then Try again.";
        }

        return $"The workspace{where} could not be built: {failure}. Try again; Open log shows the details.";
    }
}
