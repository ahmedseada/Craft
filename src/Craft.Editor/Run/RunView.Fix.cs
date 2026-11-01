// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using Craft.Abstractions;

namespace Craft.Editor.Run;

/// <summary>The last build or test that failed with errors (what "Fix with AI" fixes and "Run again" runs).</summary>
/// <param name="Configuration">The configuration that ran (its action the one that failed).</param>
/// <param name="Title">What ran ("Build: App").</param>
/// <param name="Problems">The problems its output reported.</param>
public sealed record RunFailure(RunConfiguration Configuration, string Title, IReadOnlyList<RunProblem> Problems)
{
    /// <summary>Its errors.</summary>
    public int Errors => Problems.Count(p => p.Severity == DiagnosticSeverity.Error);
}

// X part 11 item 5: "Fix with AI". When a build or a test of the view fails with errors, the view shows them with Fix with
// AI (the first registered build fix provider that offers: the AI plug-in's, one error to the chat's model, several to the
// agent; the fix arrives as diffs the user accepts) and Run again, which saves the files the accepted fix changed and runs
// the same configuration again (asked as any run is). A Problems row of the run offers Fix with AI for its one error.
public sealed partial class RunView
{
    /// <summary>The view's Fix with AI action id.</summary>
    public const string FixWithAiAction = "run.fixWithAi";

    /// <summary>The view's Stop (the fix) action id.</summary>
    public const string StopFixAction = "run.stopFix";

    /// <summary>The view's Run again action id.</summary>
    public const string RunAgainAction = "run.again";

    private RunFailure? _failure;
    private CancellationTokenSource? _fixing;
    private BuildFixResult? _fixResult;
    private string? _fixNote;

    /// <summary>The last build or test that failed with errors (cleared when the same configuration succeeds), or null.</summary>
    public RunFailure? LastFailure => _failure;

    /// <summary>The last fix's outcome (what was accepted), or null.</summary>
    public BuildFixResult? LastFix => _fixResult;

    /// <summary>Whether a fix runs now.</summary>
    public bool IsFixing => _fixing is not null;

    /// <summary>Whether a build fix provider offers to fix the last failure now (the view's and Problems' Fix with AI show).</summary>
    public bool CanFixWithAi => _failure is { } failure && _root() is { } root && BuildFixProviders.For(new BuildFixRequest(failure.Title, root, failure.Problems)) is not null;

    /// <summary>
    /// Fix with AI: the last failure's problems (or <paramref name="only"/>, a Problems row's) to the first build fix
    /// provider that offers, as an activity with Cancel; the fix arrives as diffs the user accepts. Its outcome, or null when
    /// nothing could be asked (said why).
    /// </summary>
    public async Task<BuildFixResult?> FixWithAiAsync(IReadOnlyList<RunProblem>? only, CancellationToken cancellationToken = default)
    {
        if (_fixing is not null || _root() is not { } root)
        {
            return null;
        }

        var problems = only ?? _failure?.Problems ?? _problems;
        if (problems.Count == 0)
        {
            _fixNote = "There is no failed build or test to fix.";
            Publish();
            return null;
        }

        string title = _failure?.Title ?? "The build";
        var request = new BuildFixRequest(title, root, problems);
        if (BuildFixProviders.For(request) is not { } provider)
        {
            Say(BuildFixProviders.All.Count == 0
                ? "Fix with AI needs the AI plug-in (Craft.Ai), which is not loaded."
                : "Fix with AI is turned off or has no model set (CRAFT_PLUGIN_AI_BUILDFIX, the chat's and the agent's model).", NotificationSeverity.Warning);
            return null;
        }

        var fixing = _fixing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var activity = _host.Activities.Start($"Fixing {title} with AI", ActivityKinds.Generation, cancellable: true, fixing.Token);
        _fixResult = null;
        _fixNote = null;
        Publish();
        try
        {
            var result = await provider.FixAsync(request, activity.CancellationToken).ConfigureAwait(true);
            _fixResult = result;
            _fixNote = result.Accepted ? result.Summary + " Run again to check it." : result.Summary;
            activity.Complete(result.Summary);
            return result;
        }
        catch (OperationCanceledException)
        {
            _fixNote = "The fix was stopped.";
            activity.Dispose();
            return null;
        }
#pragma warning disable CA1031 // A provider's failure is said in the view; the editor goes on.
        catch (Exception error)
#pragma warning restore CA1031
        {
            _fixNote = "The fix failed: " + error.Message;
            activity.Fail(error.Message, error);
            _host.Log(EditorLogLevel.Error, "Fix with AI failed.", error);
            return null;
        }
        finally
        {
            if (ReferenceEquals(_fixing, fixing))
            {
                _fixing = null;
            }

            fixing.Dispose();
            Publish();
        }
    }

    /// <summary>
    /// Fix with AI for one problem of the last run (a Problems row of "File.cs (build)"): <paramref name="diagnostic"/> of
    /// <paramref name="file"/>, found among the run's problems.
    /// </summary>
    public Task<BuildFixResult?> FixProblemAsync(RunProblemFile file, EditorDiagnostic diagnostic, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        int line = file.Snapshot.GetLineIndex(Math.Clamp(diagnostic.Start, 0, file.Snapshot.Length)) + 1;
        var problem = _problems.FirstOrDefault(p => string.Equals(p.File, file.Path, StringComparison.OrdinalIgnoreCase) && p.Code == diagnostic.Id && p.Message == diagnostic.Message && p.Line == line)
            ?? _problems.FirstOrDefault(p => string.Equals(p.File, file.Path, StringComparison.OrdinalIgnoreCase) && p.Code == diagnostic.Id && p.Message == diagnostic.Message);
        return problem is null ? Task.FromResult<BuildFixResult?>(null) : FixWithAiAsync([problem], cancellationToken);
    }

    /// <summary>
    /// Run again: saves the open documents the accepted fix changed (the user accepted them), then runs the failed
    /// configuration again, asked and sandboxed as any run. Null when nothing failed.
    /// </summary>
    public async Task<RunOutcome?> RunAgainAsync(CancellationToken cancellationToken = default)
    {
        if (_failure is not { } failure)
        {
            return null;
        }

        if (_fixResult is { } fix)
        {
            foreach (var document in fix.Changed)
            {
                if (document.Document.IsDirty && document.FilePath is not null)
                {
                    try
                    {
                        await _host.SaveAsync(document, null, cancellationToken).ConfigureAwait(true);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        Say($"{Path.GetFileName(document.FilePath)} could not be saved before the run: {error.Message}", NotificationSeverity.Error);
                        return null;
                    }
                }
            }
        }

        return await RunAsync(failure.Configuration, cancellationToken).ConfigureAwait(true);
    }

    // A run of a configuration ended: a build or test that failed with errors is kept for Fix with AI; the same
    // configuration's success clears it.
    private void NoteOutcome(RunConfiguration configuration, RunActions action, RunOutcome outcome)
    {
        bool failedWithErrors = outcome.End == RunEnd.Failed && outcome.Problems.Any(p => p.Severity == DiagnosticSeverity.Error)
            && action is RunActions.Build or RunActions.Test;
        if (failedWithErrors)
        {
            _failure = new RunFailure(configuration, $"{Verb(action)}: {configuration.Name}", outcome.Problems);
            _fixResult = null;
            _fixNote = null;
        }
        else if (outcome.End == RunEnd.Succeeded && _failure is { } failure && failure.Configuration.Name == configuration.Name)
        {
            _failure = null;
            _fixResult = null;
            _fixNote = $"{configuration.Name} succeeded.";
        }

        Publish();
    }

    // The failure's card: what failed, Fix with AI (or Stop while it fixes), Run again, and the fix's outcome.
    private void AddFailure(List<PanelBlock> blocks)
    {
        if (_failure is not { } failure)
        {
            if (_fixNote is { } note)
            {
                blocks.Add(new PanelText(note, PanelTextStyle.Caption) { Tone = PanelTone.Success });
            }

            return;
        }

        int errors = failure.Errors;
        bool offers = CanFixWithAi;
        bool running = _running.Count > 0;
        var buttons = new List<PanelButton>();
        if (_fixing is not null)
        {
            buttons.Add(new PanelButton("Stop", StopFixAction, PanelButtonKind.Danger) { Icon = RailIcons.Stop, Tooltip = "Stop the fix (what was accepted stays)" });
        }
        else
        {
            buttons.Add(new PanelButton("Fix with AI", FixWithAiAction, _fixResult is { Accepted: true } ? PanelButtonKind.Secondary : PanelButtonKind.Primary)
            {
                Icon = RailIcons.Agent,
                Enabled = offers,
                Tooltip = offers
                    ? errors == 1 ? "The chat's model fixes the error; the fix is a diff you accept" : "The agent fixes the errors on its copy; each file's fix is a diff you accept"
                    : BuildFixProviders.All.Count == 0 ? "Needs the AI plug-in (Craft.Ai)" : "Turned off, or no model is set (CRAFT_PLUGIN_AI_BUILDFIX)",
            });
        }

        buttons.Add(new PanelButton("Run again", RunAgainAction, _fixResult is { Accepted: true } ? PanelButtonKind.Primary : PanelButtonKind.Secondary)
        {
            Icon = RailIcons.Run,
            Enabled = !running && _fixing is null,
            Tooltip = _fixResult is { Accepted: true, Changed.Count: > 0 } ? $"Saves the files the fix changed, then runs {failure.Configuration.Name} again" : $"Runs {failure.Configuration.Name} again",
        });
        var card = new List<PanelBlock>
        {
            new PanelText(string.Create(CultureInfo.InvariantCulture, $"{errors} {(errors == 1 ? "error" : "errors")} in Problems."), PanelTextStyle.Secondary),
        };
        if (_fixing is not null)
        {
            card.Add(new PanelMeter("Fixing with AI…", -1));
        }
        else if (_fixNote is { } note)
        {
            card.Add(new PanelText(note, PanelTextStyle.Caption) { Tone = _fixResult is { Accepted: true } ? PanelTone.Success : PanelTone.Normal });
        }

        card.Add(new PanelButtons(buttons));
        blocks.Add(new PanelCard($"{failure.Title} failed", card) { Tone = PanelTone.Error, Icon = RailIcons.Build, Variable = "CRAFT_PLUGIN_AI_BUILDFIX" });
    }

    private void OnFixProvidersChanged(object? sender, EventArgs e) => _host.Dispatch("run: the build fix providers changed", Publish);
}
