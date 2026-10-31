// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>What kind of control an interaction look is for (phase X: every control answers the pointer and the keyboard).</summary>
public enum InteractionKind
{
    /// <summary>A button with no fill (the title bar's menus, a panel header's icon, the toast's Show).</summary>
    Flat,

    /// <summary>The design's secondary button: the raised fill and the strong border.</summary>
    Secondary,

    /// <summary>The design's primary button: the accent's fill and its ink.</summary>
    Primary,

    /// <summary>A row of a list (the folder tree, a picker, quick open, the problems).</summary>
    Row,

    /// <summary>An item of a menu (the title bar's, a context menu), on the raised fill.</summary>
    MenuItem,

    /// <summary>An icon of the activity rail.</summary>
    Rail,

    /// <summary>A tab (the editor's, the bottom panel's).</summary>
    Tab,

    /// <summary>A chip (the model chip, a context chip, a state chip).</summary>
    Chip,
}

/// <summary>The state a control is in.</summary>
public enum InteractionState
{
    /// <summary>At rest.</summary>
    Normal,

    /// <summary>The pointer is over it.</summary>
    Hover,

    /// <summary>It is being pressed.</summary>
    Pressed,

    /// <summary>It has the keyboard's focus (the focus ring shows).</summary>
    Focused,

    /// <summary>It cannot be used now.</summary>
    Disabled,
}

/// <summary>A control's colours in a state (0xAARRGGBB; a zero alpha draws nothing).</summary>
/// <param name="Fill">Its fill.</param>
/// <param name="Stroke">Its border (the accent while the focus ring shows).</param>
/// <param name="Text">Its text or icon.</param>
/// <param name="Ring">Whether the focus ring shows (the stroke is the accent's).</param>
public readonly record struct InteractionLook(uint Fill, uint Stroke, uint Text, bool Ring);

/// <summary>
/// Every control's hover, pressed, focus and disabled look, made from the theme's tokens (docs/design: the ground, the
/// surface, the raised fills, the selection, the borders, the text, the accent) and nothing else, so each theme and accent
/// gets them and a test can check each state differs from the one before it. Craft.Maui's <c>ShellPalette</c> applies them
/// to every button, rail item, tab, tree and list row, chip and menu item through one mechanism.
/// </summary>
public static class InteractionStyle
{
    /// <summary>The colour that draws nothing.</summary>
    public const uint None = 0x00000000;

    /// <summary>
    /// The look of a <paramref name="kind"/> control in <paramref name="state"/> under <paramref name="colors"/>;
    /// <paramref name="selected"/> for the active one of its kind (the active tab, the active file's row, the rail's open
    /// view, a menu's keyboard row); <paramref name="text"/> for a control whose resting text has a colour of its own (a
    /// losing action in the error's colour).
    /// </summary>
    public static InteractionLook Of(EditorColors colors, InteractionKind kind, InteractionState state, bool selected = false, uint? text = null)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var rest = Rest(colors, kind, selected, text);
        var look = state switch
        {
            InteractionState.Hover => Hover(colors, kind, rest, selected),
            InteractionState.Pressed => Pressed(colors, kind, rest),
            InteractionState.Focused => rest with { Stroke = colors.Accent, Ring = true },
            InteractionState.Disabled => rest with
            {
                Text = ColorContrast.Mix(rest.Text, Behind(colors, kind), 0.55),
                Fill = kind == InteractionKind.Primary ? ColorContrast.Mix(colors.Accent, colors.Surface, 0.6) : rest.Fill,
                Stroke = rest.Stroke == None ? None : colors.Border,
            },
            _ => rest,
        };
        return look;
    }

    /// <summary>The state of a control from what the pointer and the keyboard do to it, the strongest first (disabled, pressed, focus, hover).</summary>
    public static InteractionState StateOf(bool enabled, bool pressed, bool focused, bool hovered) =>
        !enabled ? InteractionState.Disabled
        : pressed ? InteractionState.Pressed
        : focused ? InteractionState.Focused
        : hovered ? InteractionState.Hover
        : InteractionState.Normal;

    // What a control sits on (its disabled text fades toward it).
    private static uint Behind(EditorColors c, InteractionKind kind) => kind switch
    {
        InteractionKind.MenuItem => c.Raised,
        InteractionKind.Rail or InteractionKind.Tab => c.Ground,
        InteractionKind.Primary => c.Accent,
        _ => c.Surface,
    };

    private static InteractionLook Rest(EditorColors c, InteractionKind kind, bool selected, uint? text) => kind switch
    {
        InteractionKind.Flat => new(None, None, text ?? c.SecondaryText, false),
        InteractionKind.Secondary => new(c.RaisedHigh, c.BorderStrong, text ?? c.Text, false),
        InteractionKind.Primary => new(c.Accent, None, text ?? c.AccentInk, false),
        InteractionKind.Row => new(selected ? c.InterfaceSelection : None, None, text ?? c.Text, false),
        InteractionKind.MenuItem => new(selected ? c.InterfaceSelection : None, None, text ?? c.Text, false),
        InteractionKind.Rail => new(selected ? c.RaisedHigh : None, None, selected ? c.Text : text ?? c.SecondaryText, false),
        InteractionKind.Tab => new(selected ? c.Surface : None, None, selected ? c.Text : text ?? c.SecondaryText, false),
        _ => new(c.Raised, c.Border, text ?? c.Text, false),
    };

    private static InteractionLook Hover(EditorColors c, InteractionKind kind, InteractionLook rest, bool selected) => kind switch
    {
        InteractionKind.Flat => rest with { Fill = c.RaisedHigh, Text = c.Text },
        InteractionKind.Secondary => rest with { Fill = ColorContrast.Mix(c.RaisedHigh, c.Text, 0.08), Stroke = c.SecondaryText },
        InteractionKind.Primary => rest with { Fill = ColorContrast.Mix(c.Accent, c.Text, 0.18) },
        InteractionKind.Row => rest with { Fill = selected ? ColorContrast.Mix(c.InterfaceSelection, c.Text, 0.06) : c.RaisedHigh },
        InteractionKind.MenuItem => rest with { Fill = selected ? ColorContrast.Mix(c.InterfaceSelection, c.Text, 0.06) : c.InterfaceSelection },
        InteractionKind.Rail => rest with { Fill = selected ? ColorContrast.Mix(c.RaisedHigh, c.Text, 0.06) : c.Raised, Text = c.Text },
        InteractionKind.Tab => rest with { Fill = selected ? ColorContrast.Mix(c.Surface, c.Text, 0.04) : ColorContrast.Mix(c.Ground, c.Surface, 0.6), Text = c.Text },
        _ => rest with { Fill = c.RaisedHigh, Stroke = c.BorderStrong },
    };

    private static InteractionLook Pressed(EditorColors c, InteractionKind kind, InteractionLook rest) => kind switch
    {
        InteractionKind.Primary => rest with { Fill = ColorContrast.Mix(c.Accent, c.Ground, 0.22) },
        InteractionKind.Secondary => rest with { Fill = ColorContrast.Mix(c.RaisedHigh, c.Ground, 0.4), Stroke = c.SecondaryText },
        InteractionKind.Tab => rest with { Fill = c.Surface, Text = c.Text },
        _ => rest with { Fill = ColorContrast.Mix(c.InterfaceSelection, c.Text, 0.12), Text = c.Text },
    };
}
