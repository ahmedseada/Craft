// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using System.Runtime.CompilerServices;
using Craft.Abstractions;
using Microsoft.Maui.Handlers;

namespace Craft.Maui.WinUI;

/// <summary>
/// Hands a <see cref="ComposerEditor"/>'s keys to its <see cref="ComposerEditor.RouteKey"/> before its TextBox takes them
/// (a PreviewKeyDown): Enter and Escape with any modifiers, and every chord with Ctrl or Alt, so the composer's bindings
/// (Enter sends, Shift+Enter a new line, Escape leaves) run and a key they take never reaches the box. Added to MAUI's
/// editor mapping once, before the first window is made.
/// </summary>
internal static class ComposerKeys
{
    private static readonly ConditionalWeakTable<Microsoft.UI.Xaml.Controls.TextBox, object> Hooked = new();
    private static int s_configured;

    public static void Configure()
    {
        if (Interlocked.Exchange(ref s_configured, 1) != 0)
        {
            return;
        }

        EditorHandler.Mapper.AppendToMapping("CraftComposerKeys", static (handler, view) =>
        {
            if (view is not ComposerEditor composer || handler.PlatformView is not Microsoft.UI.Xaml.Controls.TextBox box || Hooked.TryGetValue(box, out _))
            {
                return;
            }

            Hooked.Add(box, composer);
            box.PreviewKeyDown += (_, e) =>
            {
                try
                {
                    var key = WindowsTextInputAdapter.Map(e.Key);
                    if (key == EditorKey.None || composer.RouteKey is not { } route)
                    {
                        return;
                    }

                    var modifiers = WindowsTextInputAdapter.Modifiers();
                    bool chord = (modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0;
                    if ((key is EditorKey.Enter or EditorKey.Escape || chord) && route(key, modifiers))
                    {
                        e.Handled = true;
                    }
                }
#pragma warning disable CA1031 // Nothing may leave a WinUI callback (the owner's note 20).
                catch (Exception error)
#pragma warning restore CA1031
                {
                    e.Handled = true;
                    Craft.Core.Diagnostics.CallbackGuard.Report("the composer: a key press", error);
                }
            };
        });
    }
}
#endif
