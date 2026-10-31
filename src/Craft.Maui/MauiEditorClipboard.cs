// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Core.Diagnostics;
using Craft.Editor;
using Craft.Editor.Abstractions;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace Craft.Maui;

/// <summary>
/// <see cref="IEditorClipboard"/> over MAUI's clipboard (<see cref="Clipboard.Default"/>), on every platform MAUI runs on,
/// reached as the owner's note 20 asks: through <see cref="DispatchedClipboard"/>, so every call is queued on the UI
/// dispatcher (never made inside the key or pointer handler that asked, never off the UI thread) and a failure comes
/// back as the task's, never thrown into a platform callback. <see cref="Shared"/> is the one every view uses.
/// </summary>
public sealed class MauiEditorClipboard : IEditorClipboard
{
    private readonly DispatchedClipboard _clipboard;

    /// <summary>The clipboard queued on <paramref name="dispatcher"/> (the application's when null); a failure also goes to <paramref name="failed"/>.</summary>
    public MauiEditorClipboard(IDispatcher? dispatcher = null, Action<Exception>? failed = null)
    {
        _clipboard = new DispatchedClipboard(new Platform(), action =>
        {
            var target = dispatcher ?? Application.Current?.Dispatcher
                ?? throw new InvalidOperationException("The clipboard has no UI dispatcher: the app has no window yet.");
            if (!target.Dispatch(() => UiDispatch.Run("clipboard: copy or paste", action)))
            {
                throw new InvalidOperationException("The UI dispatcher refused the clipboard's call (the window is closing).");
            }
        }, failed ?? (error => CrashWatch.CurrentTrail?.Note($"The clipboard failed: {error.GetType().Name}: {error.Message}")));
    }

    /// <summary>The app's clipboard (the application's dispatcher, its failures noted in the crash trail).</summary>
    public static MauiEditorClipboard Shared { get; } = new();

    /// <inheritdoc />
    public Task<string?> GetTextAsync() => _clipboard.GetTextAsync();

    /// <inheritdoc />
    public Task SetTextAsync(string text) => _clipboard.SetTextAsync(text);

    /// <summary>MAUI's clipboard as it is (called only from the UI dispatcher's queue).</summary>
    private sealed class Platform : IEditorClipboard
    {
        public Task<string?> GetTextAsync() => Clipboard.Default.GetTextAsync();

        public Task SetTextAsync(string text) => Clipboard.Default.SetTextAsync(text);
    }
}
