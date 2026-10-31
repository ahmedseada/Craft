// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;

namespace Craft.Editor;

/// <summary>Edits a plug-in proposed, as the user is asked about them.</summary>
/// <param name="Document">The document they change.</param>
/// <param name="Edits">The edits, in the positions of <paramref name="Version"/>'s snapshot, in order.</param>
/// <param name="Title">The plug-in's title for them.</param>
/// <param name="Source">Who proposed them (the plug-in's name, or "host").</param>
/// <param name="Version">The snapshot version they were made for: a document changed since is not edited.</param>
/// <param name="Summary">A short description for the confirmation: how many edits, which lines, the characters removed and added, and a preview.</param>
public sealed record EditProposal(IEditorDocument Document, IReadOnlyList<TextEdit> Edits, string Title, string Source, int Version, string Summary)
{
    /// <summary>
    /// What the edits do as a line diff (the lines they touch with <c>CRAFT_DIFF_CONTEXT_LINES</c> around them): what the
    /// diff-and-accept view shows. The host makes it; empty for a proposal made elsewhere.
    /// </summary>
    public TextDiff Diff { get; init; } = TextDiff.Empty;

    /// <summary>The hunk of <see cref="Diff"/> each edit belongs to (by index, one a edit); empty: every edit in hunk 0.</summary>
    public IReadOnlyList<int> HunkOfEdit { get; init; } = [];

    /// <summary>How the edits were placed (<see cref="ProposedEdits.Placement"/>), or null.</summary>
    public string? Placement { get; init; }

    /// <summary>Where they came from (<see cref="ProposedEdits.Origin"/>, else <see cref="Source"/>).</summary>
    public string? Origin { get; init; }

    /// <summary>
    /// The language's check of the change before it is accepted (diagnostics now and after; <c>CRAFT_DIFF_CHECK</c>),
    /// running while the user looks; its result is null when the language has no semantic layer or cannot tell.
    /// </summary>
    public Task<ProposalCheck?> Check { get; init; } = Task.FromResult<ProposalCheck?>(null);

    /// <summary>The edits of the hunks <paramref name="answers"/> accepts (one answer a hunk), in order.</summary>
    public TextEdit[] Accepted(IReadOnlyList<bool> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var accepted = new List<TextEdit>(Edits.Count);
        for (int i = 0; i < Edits.Count; i++)
        {
            int hunk = i < HunkOfEdit.Count ? HunkOfEdit[i] : 0;
            if (hunk < answers.Count && answers[hunk])
            {
                accepted.Add(Edits[i]);
            }
        }

        return [.. accepted];
    }

    /// <summary>
    /// The hunk of <paramref name="diff"/> each of <paramref name="edits"/> belongs to: the first whose old lines meet the
    /// edit's lines (an edit that changes no line goes with the nearest hunk before it).
    /// </summary>
    public static int[] HunksOf(ITextSnapshot snapshot, IReadOnlyList<TextEdit> edits, TextDiff diff)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(diff);
        var result = new int[edits.Count];
        var hunks = diff.Hunks;
        for (int i = 0; i < edits.Count; i++)
        {
            int first = snapshot.GetLineIndex(edits[i].Start) + 1;
            int last = snapshot.GetLineIndex(edits[i].End) + 1;
            int found = 0;
            for (int h = 0; h < hunks.Count; h++)
            {
                int start = hunks[h].OldStart;
                int end = hunks[h].OldStart + Math.Max(hunks[h].OldCount, 1) - 1;
                if (start <= last)
                {
                    found = h;
                }

                if (start <= last && end >= first)
                {
                    break;
                }
            }

            result[i] = found;
        }

        return result;
    }

    /// <summary>The summary of <paramref name="edits"/> to <paramref name="snapshot"/>, its preview cut at <paramref name="previewCharacters"/>.</summary>
    public static string Summarize(ITextSnapshot snapshot, string? filePath, IReadOnlyList<TextEdit> edits, int previewCharacters)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(edits);
        if (edits.Count == 0)
        {
            return "No change.";
        }

        int removed = 0, added = 0;
        foreach (var edit in edits)
        {
            removed += edit.Length;
            added += edit.Text.Length;
        }

        int firstLine = snapshot.GetLineIndex(edits[0].Start) + 1;
        int lastLine = snapshot.GetLineIndex(edits[^1].End) + 1;
        var text = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;
        text.Append(culture, $"{edits.Count} {(edits.Count == 1 ? "edit" : "edits")} to {(filePath is null ? "the untitled document" : Path.GetFileName(filePath))}, ");
        if (firstLine == lastLine)
        {
            text.Append(culture, $"line {firstLine}");
        }
        else
        {
            text.Append(culture, $"lines {firstLine}-{lastLine}");
        }

        text.Append(culture, $": {removed} characters removed, {added} added.");
        var first = edits[0];
        if (previewCharacters > 0 && first.Text.Length > 0)
        {
            bool cut = first.Text.Length > previewCharacters;
            text.Append("\n\n").Append(first.Text.AsSpan(0, Math.Min(previewCharacters, first.Text.Length))).Append(cut ? "…" : "");
        }

        return text.ToString();
    }
}

/// <summary>What the language reports before and after a proposed change (the diff view's "before you accept").</summary>
/// <param name="ErrorsBefore">Errors in the document now.</param>
/// <param name="WarningsBefore">Warnings now.</param>
/// <param name="ErrorsAfter">Errors with the change applied.</param>
/// <param name="WarningsAfter">Warnings with the change applied.</param>
/// <param name="FirstErrors">The first errors after the change (id and message), at most five.</param>
public sealed record ProposalCheck(int ErrorsBefore, int WarningsBefore, int ErrorsAfter, int WarningsAfter, IReadOnlyList<string> FirstErrors)
{
    /// <summary>The check of <paramref name="edits"/> to <paramref name="snapshot"/> through <paramref name="session"/>; null when it cannot tell or fails.</summary>
    public static async Task<ProposalCheck?> RunAsync(ISemanticLanguageSession session, ITextSnapshot snapshot, IReadOnlyList<TextEdit> edits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        try
        {
            var before = session.Diagnostics;
            if (await session.DiagnoseEditsAsync(snapshot, edits, cancellationToken).ConfigureAwait(false) is not { } after)
            {
                return null;
            }

            var first = new List<string>(5);
            foreach (var item in after.Items)
            {
                if (item.Severity == DiagnosticSeverity.Error && first.Count < 5)
                {
                    first.Add($"{item.Id}: {item.Message}");
                }
            }

            return new ProposalCheck(before.Errors, before.Warnings, after.Errors, after.Warnings, first);
        }
#pragma warning disable CA1031 // A check that fails leaves the diff without it; the user still decides.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }
}

/// <summary>What activating one plug-in did in a host.</summary>
/// <param name="Name">The plug-in's name.</param>
/// <param name="Active">Whether it is active.</param>
/// <param name="Error">Why not, when its <see cref="IEditorPlugin.Activate"/> threw.</param>
/// <param name="Milliseconds">How long its activation took.</param>
public sealed record PluginActivation(string Name, bool Active, string? Error, double Milliseconds);

/// <summary>A line of the host's log.</summary>
/// <param name="Time">When it was written.</param>
/// <param name="Level">How much it matters.</param>
/// <param name="Source">Who wrote it (a plug-in's name, or "host").</param>
/// <param name="Message">What it says.</param>
/// <param name="Error">The error it reports, or null.</param>
public sealed record EditorLogEntry(DateTimeOffset Time, EditorLogLevel Level, string Source, string Message, Exception? Error);

/// <summary>A measure recorded through the host (telemetry).</summary>
public sealed class EditorMeasureEventArgs(string source, string metric, double value, string unit) : EventArgs
{
    /// <summary>Who recorded it.</summary>
    public string Source { get; } = source;

    /// <summary>What it measures.</summary>
    public string Metric { get; } = metric;

    /// <summary>Its value.</summary>
    public double Value { get; } = value;

    /// <summary>Its unit.</summary>
    public string Unit { get; } = unit;
}

/// <summary>Long work shown by the host.</summary>
public sealed class EditorProgressItem : IEditorProgress
{
    private readonly EditorHost _host;

    internal EditorProgressItem(EditorHost host, string title, string source)
    {
        _host = host;
        Title = title;
        Source = source;
    }

    /// <inheritdoc />
    public string Title { get; }

    /// <summary>Who started it.</summary>
    public string Source { get; }

    /// <summary>How far, from 0 to 1, or null when not known.</summary>
    public double? Fraction { get; private set; }

    /// <summary>What it does now, or null.</summary>
    public string? Message { get; private set; }

    /// <summary>Whether it ended.</summary>
    public bool IsDone { get; private set; }

    /// <inheritdoc />
    public void Report(double? fraction, string? message = null)
    {
        if (IsDone)
        {
            return;
        }

        Fraction = fraction is { } f ? Math.Clamp(f, 0, 1) : null;
        Message = message;
        _host.OnProgressChanged(this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!IsDone)
        {
            IsDone = true;
            _host.OnProgressEnded(this);
        }
    }
}

/// <summary>
/// An event's handlers, copied on write so raising walks an array without a lock or an allocation, each handler called
/// alone so one that throws is reported and the others still run.
/// </summary>
internal sealed class HandlerList<THandler> where THandler : Delegate
{
    private readonly Lock _gate = new();
    private THandler[] _items = [];

    public THandler[] Items => Volatile.Read(ref _items);

    public bool IsEmpty => Items.Length == 0;

    public void Add(THandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        lock (_gate)
        {
            _items = [.. _items, handler];
        }
    }

    public void Remove(THandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        lock (_gate)
        {
            int index = Array.LastIndexOf(_items, handler);
            if (index >= 0)
            {
                _items = [.. _items.AsSpan(0, index), .. _items.AsSpan(index + 1)];
            }
        }
    }
}
