// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Editor.Abstractions;

/// <summary>
/// The platform's file dialogs: pick a folder, a file to open, a path to save to. Each platform implements it under its
/// <c>Platforms/</c> folder (Windows: <c>Craft.Maui.WinUI.WindowsFileDialogs</c>; Android in phase M); the shell and the
/// tests use only this contract. Each returns null when the user cancels.
/// </summary>
public interface IFileDialogs
{
    /// <summary>
    /// Asks for a folder, opening on <paramref name="startFolder"/> (the workspace; null: the user's documents folder), a
    /// real folder the dialog can select at once; its full path, or null.
    /// </summary>
    Task<string?> PickFolderAsync(string? startFolder = null, CancellationToken cancellationToken = default);

    /// <summary>Asks for a file to open, opening on <paramref name="startFolder"/> (null: the user's documents folder); its full path, or null.</summary>
    Task<string?> PickFileAsync(string? startFolder = null, CancellationToken cancellationToken = default);

    /// <summary>Asks where to save, suggesting <paramref name="suggestedName"/> (in <paramref name="folder"/> when given); the full path, or null.</summary>
    Task<string?> PickSavePathAsync(string suggestedName, string? folder, CancellationToken cancellationToken = default);
}

/// <summary>What to do with a document's unsaved changes when it closes.</summary>
public enum UnsavedChoice
{
    /// <summary>Save it, then close (a document with no file asks where first).</summary>
    Save,

    /// <summary>Close without saving.</summary>
    Discard,

    /// <summary>Keep it open.</summary>
    Cancel,
}

/// <summary>
/// The prompts and pages the shell (<see cref="Shell.ShellModel"/>) asks the UI for: the unsaved-changes question, a
/// pick from a list, the settings page and a message. Craft.Maui implements it with MAUI's dialogs (no platform code); the
/// tests with a fake.
/// </summary>
public interface IShellUi
{
    /// <summary>Asks what to do with the unsaved changes of the document titled <paramref name="title"/>.</summary>
    Task<UnsavedChoice> AskUnsavedAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>Asks for one of <paramref name="items"/> under <paramref name="title"/>; its index, or -1 when none was picked.</summary>
    Task<int> PickAsync(string title, IReadOnlyList<string> items, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for one of <paramref name="request"/>'s items in a quick-open list (X part 9: the theme picker, the status bar's
    /// pickers): typing filters it, the arrows move and each row moved to is told to <see cref="QuickPickRequest.Moved"/>
    /// (a live preview), Enter picks, Escape cancels; its index, or -1. A UI without such a list answers with
    /// <see cref="PickAsync"/> (no preview).
    /// </summary>
    Task<int> QuickPickAsync(QuickPickRequest request, CancellationToken cancellationToken = default) =>
        request is null ? Task.FromResult(-1) : PickAsync(request.Title, request.Texts(), cancellationToken);

    /// <summary>Shows a message; completes when the user dismissed it.</summary>
    Task ShowMessageAsync(string title, string message);

    /// <summary>
    /// Asks for a text (a new file's name, a rename) under <paramref name="title"/>, the field starting with
    /// <paramref name="initial"/>; the text, or null when it was cancelled. A UI that cannot ask answers null (nothing is done).
    /// </summary>
    Task<string?> PromptAsync(string title, string text, string placeholder, string initial = "", string ok = "OK", CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    /// <summary>
    /// Asks the user to confirm <paramref name="action"/> (a delete: <paramref name="danger"/> draws it in the error's
    /// colour) with Cancel beside it; true only when the action was chosen. A UI that cannot ask answers false (nothing is done).
    /// </summary>
    Task<bool> ConfirmAsync(string title, string text, string action, bool danger = false, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

/// <summary>
/// The system's file manager and trash, each platform's under its <c>Platforms/</c> folder (Windows: File Explorer and the
/// Recycle Bin); the shell's Reveal and Delete use it, off the UI thread. A platform without one passes null: Reveal is
/// then not offered and Delete removes the file for good (and the confirmation says so).
/// </summary>
public interface IShellFiles
{
    /// <summary>The name of the system's file manager as its menu item says it ("Reveal in File Explorer").</summary>
    string RevealTitle { get; }

    /// <summary>Shows <paramref name="path"/> (a file or folder) selected in the system's file manager.</summary>
    Task RevealAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The trash's name as the confirmation says it ("the Recycle Bin"), or null when there is none.</summary>
    string? TrashName { get; }

    /// <summary>Moves <paramref name="path"/> (a file or folder) to the trash; false when this system has none (the caller deletes it).</summary>
    Task<bool> TrashAsync(string path, CancellationToken cancellationToken = default);
}
