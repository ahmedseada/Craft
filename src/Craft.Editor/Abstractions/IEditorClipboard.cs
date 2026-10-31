// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Editor.Abstractions;

/// <summary>
/// The clipboard the editor copies to and pastes from: the platform's (Craft.Maui wraps MAUI's), or a fake in tests. Both
/// calls are asynchronous, so the UI thread never waits on the platform.
/// </summary>
public interface IEditorClipboard
{
    /// <summary>The clipboard's text, or null when it holds none.</summary>
    Task<string?> GetTextAsync();

    /// <summary>Puts <paramref name="text"/> on the clipboard.</summary>
    Task SetTextAsync(string text);
}
