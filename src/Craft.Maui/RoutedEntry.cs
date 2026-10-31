// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Microsoft.Maui.Controls;

namespace Craft.Maui;

/// <summary>
/// A one-line text box whose navigation keys go to its owner before the box takes them (the palette's and quick open's
/// input: Up, Down, Page Up, Page Down, Enter, Escape; the find overlay's: Enter, Shift+Enter, Alt+C): the platform hands
/// them to <see cref="RouteKey"/> (on Windows a PreviewKeyDown, <c>Platforms/Windows/EntryKeys.cs</c>), and a key it
/// takes never reaches the box. Where no platform hook runs, Enter still arrives through <see cref="Entry.Completed"/>.
/// </summary>
public sealed class RoutedEntry : Entry
{
    /// <summary>A routed box; Enter falls back to <see cref="Entry.Completed"/> where the platform has no hook.</summary>
    public RoutedEntry()
    {
        Completed += (_, _) =>
        {
            if (!KeysHooked)
            {
                RouteKey?.Invoke(EditorKey.Enter, KeyModifiers.None);
            }
        };
    }

    /// <summary>Takes a key with its modifiers: true when the owner took it (the box then ignores it).</summary>
    public Func<EditorKey, KeyModifiers, bool>? RouteKey { get; set; }

    /// <summary>Whether the platform hands the keys over (set by its hook once attached).</summary>
    public bool KeysHooked { get; set; }

    /// <summary>The keys a platform hook hands over: the list's and the overlay's keys, and every chord with Ctrl or Alt.</summary>
    public static bool IsRouted(EditorKey key, KeyModifiers modifiers) =>
        key is EditorKey.Up or EditorKey.Down or EditorKey.PageUp or EditorKey.PageDown or EditorKey.Enter or EditorKey.Escape or EditorKey.F3
        || (modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) != 0;
}
