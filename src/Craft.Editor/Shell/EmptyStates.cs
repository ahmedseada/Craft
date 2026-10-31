// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Workspaces;

namespace Craft.Editor.Shell;

/// <summary>An action an empty state offers: a command to run, or a folder to open (a recent one).</summary>
/// <param name="Label">What it shows ("Open folder… (Ctrl+Shift+O)").</param>
/// <param name="CommandId">The command it runs, or null.</param>
/// <param name="Path">The folder it opens, or null.</param>
/// <param name="Primary">Whether it is the first thing to do (drawn as the primary button).</param>
public sealed record EmptyAction(string Label, string? CommandId = null, string? Path = null, bool Primary = false);

/// <summary>What a place with nothing in it says (phase X: empty states that say what to do).</summary>
/// <param name="Title">The line in front ("No folder is open").</param>
/// <param name="Text">What to do, in a sentence or two.</param>
/// <param name="Actions">The actions, in order.</param>
public sealed record EmptyState(string Title, string Text, IReadOnlyList<EmptyAction> Actions)
{
    /// <summary>A second list under the actions (the recent folders), with its caption; empty for none.</summary>
    public IReadOnlyList<EmptyAction> Recent { get; init; } = [];
}

/// <summary>
/// The empty states of the shell (phase X), chosen headless from what the shell holds, so each says what to do there and
/// names the keys in effect: the explorer with no folder (Open folder, the recent folders), Problems with none (or why
/// there can be none), the chat and the agent before the first message (what they do, which model answers), the
/// notification centre with nothing in it, and a search that found nothing.
/// </summary>
public static class ShellEmptyStates
{
    /// <summary>The explorer with no folder: Open folder, Open a file, and up to <paramref name="recentLimit"/> recent folders.</summary>
    public static EmptyState Explorer(EditorHost host, RecentList recent, int recentLimit = 5)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(recent);
        var folders = new List<EmptyAction>();
        foreach (var entry in recent.Entries)
        {
            if (entry.Kind == RecentKind.Folder && folders.Count < recentLimit)
            {
                folders.Add(new EmptyAction(Path.GetFileName(Path.TrimEndingDirectorySeparator(entry.Path)) is { Length: > 0 } name ? name : entry.Path, Path: entry.Path));
            }
        }

        return new EmptyState("No folder is open",
            "Open a folder to see its files here, find them by name (Ctrl+P) and let the C# plug-in read its projects.",
            [
                new EmptyAction(Label(host, "Open folder…", ShellCommandIds.OpenFolder), ShellCommandIds.OpenFolder, Primary: true),
                new EmptyAction(Label(host, "Open a file…", ShellCommandIds.OpenFile), ShellCommandIds.OpenFile),
            ])
        { Recent = folders };
    }

    /// <summary>
    /// The Problems tab with nothing listed for <paramref name="model"/> (the active editor, or null), the file titled
    /// <paramref name="fileName"/>: no file open, a file no language checks, the check still running, or none found.
    /// </summary>
    public static EmptyState Problems(EditorModel? model, string? fileName)
    {
        if (model is null)
        {
            return new EmptyState("No problems to show", "Open a file: the errors and warnings its language finds show here as you type.", []);
        }

        if (model.SemanticSession is not { } session)
        {
            return new EmptyState($"No checks for {fileName ?? "this file"}",
                $"{model.LanguageName} has no language plug-in that finds problems; C# files are checked by the C# plug-in (Plug-ins page).",
                [new EmptyAction("Show the plug-ins", ShellCommandIds.Plugins)]);
        }

        if (session.IsSemanticDeferred)
        {
            return new EmptyState("Not checked yet", "The file is large, so its problems wait: run 'Enable semantics' from the command palette (Ctrl+Shift+P) to check it.",
                [new EmptyAction("Show all commands", ShellCommandIds.CommandPalette)]);
        }

        if (!session.Telemetry.IsBuilt)
        {
            return new EmptyState("Checking…", $"{session.Telemetry.Engine ?? model.LanguageName} is reading the workspace; problems show here when it is ready.", []);
        }

        return new EmptyState($"No problems in {fileName ?? "this file"}", "Errors, warnings and suggestions show here as you type; the status bar's count opens this tab.", []);
    }

    /// <summary>
    /// A conversation panel before its first message: what it does (the panel's <see cref="IConversationPanel.EmptyText"/>,
    /// else a sentence from its title and placeholder) and which model answers (<paramref name="model"/>, the first
    /// provider's status), with a way to pick or add one.
    /// </summary>
    public static EmptyState Conversation(IConversationPanel panel, ModelProviderStatus? model)
    {
        ArgumentNullException.ThrowIfNull(panel);
        string does = panel.EmptyText ?? $"{panel.Title}: {panel.Placeholder}";
        // Note 25: the panel's own role (the chat's, the agent's), each with its own model and device.
        string? key = panel.ModelRoleKey;
        var role = model?.RoleOf(key);
        if (role is { Enabled: false })
        {
            IReadOnlyList<EmptyAction> open = role.SettingsCommand is { } command ? [new EmptyAction($"Open the {role.Role.ToLowerInvariant()}'s settings", command, Primary: true)] : [];
            return new EmptyState(panel.Title, $"{role.Role} is turned off: it loads no model and answers nothing. Turn it on in its settings.", open);
        }

        string? name = role is null ? model?.ChatModel : role.Model;
        string answers = name is not null
            ? $"{name} answers{(ShellText.PlannedDevice(model, key) is { } device ? " on " + device : "")}{(role is { InheritsModel: true, Inherits: { } other } ? $" (the {other.ToLowerInvariant()}'s model)" : "")}: pick another with the model chip under the input."
            : "No model is set yet: pick one with the model chip under the input, or add one on the Models and devices page.";
        IReadOnlyList<EmptyAction> actions = name is null ? [new EmptyAction("Models and devices", ShellCommandIds.Models, Primary: true)] : [];
        return new EmptyState(panel.Title, $"{does}\n{answers}", actions);
    }

    /// <summary>The notification centre with nothing in it.</summary>
    public static EmptyState Notifications() => new("All quiet",
        "Running operations (a model loading, a build, an agent's run) show here with their progress and Cancel, then how they ended; the plug-ins' messages too.", []);

    /// <summary>
    /// Quick open's list when nothing matches <paramref name="query"/>: a line to go to, a command, a file of the folder, or
    /// a folder to open first (<paramref name="hasFolder"/>, <paramref name="hasEditor"/>, <paramref name="indexing"/>).
    /// </summary>
    public static EmptyState Search(EditorHost host, string query, bool hasFolder, bool hasEditor, bool indexing)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(query);
        var trimmed = query.TrimStart();
        if (QuickOpen.IsLineQuery(query))
        {
            return hasEditor
                ? new EmptyState("Go to a line", "Type a line number (:42, or :42:7 for a column), then Enter.", [])
                : new EmptyState("No file is open", "Open a file to go to one of its lines.", [new EmptyAction(Label(host, "Open a file…", ShellCommandIds.OpenFile), ShellCommandIds.OpenFile, Primary: true)]);
        }

        if (trimmed.StartsWith('>'))
        {
            return new EmptyState($"No command matches '{trimmed[1..].Trim()}'", "Try another word, or see every command with its keys.",
                [new EmptyAction(Label(host, "Show all commands", ShellCommandIds.CommandPalette), ShellCommandIds.CommandPalette)]);
        }

        if (!hasFolder)
        {
            return new EmptyState("No folder is open", "Open a folder to find its files by name; type '>' for a command, ':' for a line.",
                [new EmptyAction(Label(host, "Open folder…", ShellCommandIds.OpenFolder), ShellCommandIds.OpenFolder, Primary: true)]);
        }

        if (indexing)
        {
            return new EmptyState("Listing the folder's files…", "The files show as soon as they are listed.", []);
        }

        return new EmptyState(trimmed.Length == 0 ? "Type a file's name" : $"No file matches '{trimmed}'",
            "Letters in order find a file (\"shmo\" finds ShellModel.cs); type '>' for a command, ':' for a line.", []);
    }

    // An action's label with the keys that run it now.
    private static string Label(EditorHost host, string text, string commandId) => ShellTooltips.For(host, text, commandId);
}
