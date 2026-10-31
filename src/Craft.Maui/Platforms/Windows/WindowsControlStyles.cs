// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

#if WINDOWS
using Craft.Editor.Shell;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MauiElement = Microsoft.Maui.Controls.Element;
using WinThickness = Microsoft.UI.Xaml.Thickness;

namespace Craft.Maui.WinUI;

/// <summary>
/// The WinUI side of Craft's controls, as handler mappings (once per process, before the window):
/// <list type="bullet">
/// <item>the owner's note 12: a TextBox (MAUI's Entry and Editor) draws no border, no fill and no focus underline of its
/// own in any state (rest, pointer over, focused, disabled), so the only box is <see cref="CraftInput"/>'s border;</item>
/// <item>the owner's note 10: the dialog's card (<see cref="CraftDialog.CardClassId"/>) keeps the Tab key's focus
/// moves inside it (<see cref="KeyboardNavigationMode.Cycle"/>), and a picker's rows (<see cref="CraftDialog.RowClassId"/>)
/// are left-aligned.</item>
/// </list>
/// </summary>
public static class WindowsControlStyles
{
    private static int s_configured;

    // The TextBox template's resources for its border and fill, in every visual state.
    private static readonly string[] BorderThicknesses = ["TextControlBorderThemeThickness", "TextControlBorderThemeThicknessFocused"];

    private static readonly string[] Brushes =
    [
        "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
        "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled",
    ];

    /// <summary>Appends the mappings (once).</summary>
    public static void Configure()
    {
        if (Interlocked.Exchange(ref s_configured, 1) != 0)
        {
            return;
        }

        EntryHandler.Mapper.AppendToMapping("CraftInput", (handler, _) => Borderless(handler.PlatformView));
        EditorHandler.Mapper.AppendToMapping("CraftInput", (handler, _) => Borderless(handler.PlatformView));
        BorderHandler.Mapper.AppendToMapping("CraftDialog", (handler, view) =>
        {
            if (view is MauiElement { ClassId: CraftDialog.CardClassId } && handler.PlatformView is UIElement card)
            {
                card.TabFocusNavigation = KeyboardNavigationMode.Cycle;
            }
        });
        ButtonHandler.Mapper.AppendToMapping("CraftDialogRow", (handler, view) =>
        {
            if (view is MauiElement { ClassId: CraftDialog.RowClassId } && handler.PlatformView is { } button)
            {
                button.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left;
            }
        });
    }

    private static void Borderless(Microsoft.UI.Xaml.Controls.TextBox box)
    {
        var none = new WinThickness(0);
        var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in BorderThicknesses)
        {
            box.Resources[key] = none;
        }

        foreach (var key in Brushes)
        {
            box.Resources[key] = clear;
        }

        box.BorderThickness = none;
        box.Background = clear;
        box.BorderBrush = clear;
        box.Padding = new WinThickness(0, 4, 0, 4);
        box.MinHeight = 0;
    }
}

/// <summary>
/// Windows' code font for the drawn surfaces (the owner's note 14): the MAUI canvas draws through Win2D, whose text format
/// takes a family by the system's name and does not see a MAUI font by its alias, so the bundled JetBrains Mono (copied
/// beside the program into <see cref="CodeFonts.Folder"/>) is named by its files: a <c>file:///</c> reference first, then
/// the app's <c>ms-appx:///</c> one, each "…#JetBrains Mono". <see cref="CodeFontPicker"/> measures each on the canvas and
/// takes the first whose cell is the file's own advance, else a monospaced fallback.
/// </summary>
public sealed class WindowsDrawnFonts : IDrawnFonts
{
    /// <summary>The faces, read once (the font file's metrics) when the platform is made, before the window.</summary>
    public WindowsDrawnFonts()
    {
        string folder = Path.Combine(AppContext.BaseDirectory, CodeFonts.Folder);
        var forms = new List<CodeFontFaces>(2);
        if (CodeFonts.BundledFaces(folder, (path, family) => new Uri(path).AbsoluteUri + "#" + family) is { } file)
        {
            forms.Add(file);
        }

        if (CodeFonts.BundledFaces(folder, (path, family) => $"ms-appx:///{CodeFonts.Folder}/{Path.GetFileName(path)}#{family}") is { } app)
        {
            forms.Add(app with { Source = "the bundled JetBrains Mono (ms-appx)" });
        }

        Bundled = forms;
    }

    /// <inheritdoc />
    public IReadOnlyList<CodeFontFaces> Bundled { get; }
}
#endif
