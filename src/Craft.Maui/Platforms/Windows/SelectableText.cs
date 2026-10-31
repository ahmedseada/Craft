// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using Microsoft.Maui.Handlers;

namespace Craft.Maui.WinUI;

/// <summary>
/// Makes a <see cref="SelectableLabel"/>'s text selectable on Windows: its TextBlock takes text selection, so the chat's
/// and the agent's messages can be selected with the mouse or the keyboard and copied (Ctrl+C, the context menu's Copy).
/// Added to MAUI's label mapping once, before the first window is made.
/// </summary>
internal static class SelectableText
{
    private static int s_configured;

    public static void Configure()
    {
        if (Interlocked.Exchange(ref s_configured, 1) != 0)
        {
            return;
        }

        LabelHandler.Mapper.AppendToMapping("CraftSelectable", (handler, view) =>
        {
            if (view is SelectableLabel)
            {
                handler.PlatformView.IsTextSelectionEnabled = true;
            }
        });
    }
}
#endif
