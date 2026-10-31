// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Editor.Abstractions;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace Craft.Maui;

/// <summary><see cref="IEditorClipboard"/> over MAUI's clipboard (<see cref="Clipboard.Default"/>), on every platform MAUI runs on.</summary>
public sealed class MauiEditorClipboard : IEditorClipboard
{
    /// <inheritdoc />
    public Task<string?> GetTextAsync() => Clipboard.Default.GetTextAsync();

    /// <inheritdoc />
    public Task SetTextAsync(string text) => Clipboard.Default.SetTextAsync(text);
}
