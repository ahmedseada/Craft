// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core.Settings;
using Craft.Core.Workspaces;

namespace Craft.Editor.Shell;

/// <summary>A recent folder as the welcome page lists it: its name, where it is, and the full path.</summary>
/// <param name="Name">The folder's name.</param>
/// <param name="Place">The folder it is in.</param>
/// <param name="Path">Its full path.</param>
/// <param name="Opened">When it was last opened.</param>
public sealed record WelcomeFolder(string Name, string Place, string Path, DateTimeOffset Opened);

/// <summary>A shortcut the welcome page shows: what it does and its keys as bound now (the user's remap shows).</summary>
/// <param name="Title">What it does.</param>
/// <param name="CommandId">The command it runs.</param>
/// <param name="Keys">Its keys now, or null when none are bound.</param>
public sealed record WelcomeShortcut(string Title, string CommandId, string? Keys);

/// <summary>
/// The welcome page (phase X; docs/design's tokens): Open folder and New file, the recent folders (the user's recent list,
/// each removable with Undo in the inline bar), the main shortcuts read from the key bindings, and the "Show at start"
/// switch (<c>CRAFT_WELCOME_AT_START</c>, written to the user's settings). Headless; the app draws it.
/// </summary>
public sealed class WelcomePageModel
{
    /// <summary>The variable of the "Show at start" switch.</summary>
    public const string ShowAtStartVariable = "CRAFT_WELCOME_AT_START";

    private readonly ShellModel _shell;

    /// <summary>The page over <paramref name="shell"/>.</summary>
    public WelcomePageModel(ShellModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    /// <summary>The commands whose keys the page lists, in order, with the title it gives each.</summary>
    public static IReadOnlyList<(string Title, string CommandId)> MainCommands { get; } =
    [
        ("Show all commands", ShellCommandIds.CommandPalette),
        ("Go to a file", ShellCommandIds.QuickOpen),
        ("Go to a line", ShellCommandIds.GoToLine),
        ("Open a folder", ShellCommandIds.OpenFolder),
        ("New file", ShellCommandIds.NewFile),
        ("Find", "craft.find"),
        ("Replace", "craft.replace"),
        ("Show or hide the terminal", Craft.Editor.Terminal.TerminalCommandIds.Toggle),
        ("Show or hide the folder tree", ShellCommandIds.ToggleTree),
        ("Settings", ShellCommandIds.Settings),
        ("Keyboard shortcuts", ShellCommandIds.KeyboardShortcuts),
    ];

    /// <summary>The recent folders, newest first.</summary>
    public IReadOnlyList<WelcomeFolder> RecentFolders =>
    [
        .. _shell.Recent.Entries.Where(e => e.Kind == RecentKind.Folder).Select(e =>
            new WelcomeFolder(Path.GetFileName(e.Path) is { Length: > 0 } name ? name : e.Path, Path.GetDirectoryName(e.Path) ?? "", e.Path, e.Opened)),
    ];

    /// <summary>The main shortcuts with their keys as bound now (a command without keys shows none).</summary>
    public IReadOnlyList<WelcomeShortcut> Shortcuts =>
        [.. MainCommands.Select(c => new WelcomeShortcut(c.Title, c.CommandId, _shell.Host.KeysOf(c.CommandId)))];

    /// <summary>Whether the page opens at start (<c>CRAFT_WELCOME_AT_START</c>).</summary>
    public bool ShowAtStart => _shell.Options.WelcomeAtStart;

    /// <summary>Why the switch cannot change it here (the environment sets it), or null.</summary>
    public string? ShowAtStartReadOnly => _shell.Settings.SourceOf(ShowAtStartVariable) == SettingSource.Environment
        ? $"The environment sets {ShowAtStartVariable}, which overrides the settings files."
        : null;

    /// <summary>Sets the switch in the user's settings; null when done, else why not.</summary>
    public string? SetShowAtStart(bool show) =>
        ShowAtStartReadOnly ?? _shell.ApplySetting(SettingLayer.User, ShowAtStartVariable, show ? null : "false");

    /// <summary>Takes <paramref name="path"/> off the recent folders, with Undo in the inline bar.</summary>
    public bool Remove(string path) => _shell.RemoveRecent(path);

    /// <summary>Opens a recent folder as the workspace (one that is gone is taken off the list, said so).</summary>
    public async Task<bool> OpenAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!await Task.Run(() => Directory.Exists(path)))
        {
            _shell.Recent.Remove(path);
            await _shell.Ui.ShowMessageAsync("Recent", $"{path} no longer exists; it was taken off the list.");
            return false;
        }

        return await _shell.OpenFolderAsync(path);
    }

    /// <summary>Open folder: the platform's folder picker (the shell's command).</summary>
    public Task<bool> OpenFolderAsync() => _shell.Host.ExecuteCommandAsync(ShellCommandIds.OpenFolder);

    /// <summary>New file: an untitled document.</summary>
    public DocumentTab NewFile() => _shell.NewFile();
}
