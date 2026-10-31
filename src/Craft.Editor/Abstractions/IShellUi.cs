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

    /// <summary>Shows a message; completes when the user dismissed it.</summary>
    Task ShowMessageAsync(string title, string message);
}
