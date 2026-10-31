// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using System.Runtime.CompilerServices;
using Craft.Abstractions;
using Microsoft.Maui.Handlers;

namespace Craft.Maui.WinUI;

/// <summary>
/// Hands a <see cref="RoutedEntry"/>'s navigation keys to its <see cref="RoutedEntry.RouteKey"/> before its TextBox takes
/// them (a PreviewKeyDown, as <see cref="ComposerKeys"/> does for the composer): the keys <see cref="RoutedEntry.IsRouted"/>
/// names; a key the owner takes never reaches the box. Added to MAUI's entry mapping once, before the first window.
/// </summary>
internal static class EntryKeys
{
    private static readonly ConditionalWeakTable<Microsoft.UI.Xaml.Controls.TextBox, object> Hooked = new();
    private static int s_configured;

    public static void Configure()
    {
        if (Interlocked.Exchange(ref s_configured, 1) != 0)
        {
            return;
        }

        EntryHandler.Mapper.AppendToMapping("CraftRoutedEntryKeys", static (handler, view) =>
        {
            if (view is not RoutedEntry entry || handler.PlatformView is not Microsoft.UI.Xaml.Controls.TextBox box || Hooked.TryGetValue(box, out _))
            {
                return;
            }

            Hooked.Add(box, entry);
            entry.KeysHooked = true;
            box.PreviewKeyDown += (_, e) => Craft.Core.Diagnostics.CallbackGuard.Run("a routed text box: a key press", () =>
            {
                var key = WindowsTextInputAdapter.Map(e.Key);
                if (key == EditorKey.None || entry.RouteKey is not { } route)
                {
                    return;
                }

                var modifiers = WindowsTextInputAdapter.Modifiers();
                if (RoutedEntry.IsRouted(key, modifiers) && route(key, modifiers))
                {
                    e.Handled = true;
                }
            });
        });
    }
}
#endif
