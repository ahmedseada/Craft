// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core;
using Craft.Core.Settings;
using Craft.Editor.Shell;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// B8: the splitters' behaviour, headless (<see cref="SplitterModel"/>, what <c>Craft.Maui.SplitPanel</c> runs): a drag
/// measured in the window's coordinates and clamped, snapping closed below the smallest size and open again past it, a
/// double-click back to the default, the keys' steps, Escape putting it back, and the options that set it.
/// </summary>
[TestGroup("Layout")]
public static class SplitterTests
{
    private static SplitterModel Model(int grows, bool vertical = true) => new(grows, vertical)
    {
        Minimum = 120,
        DefaultSize = 260,
        KeyStep = 10,
        Clamp = size => Math.Clamp(size, 120, 800),        // as PanelLayout.Clamp with 920 units of room
    };

    [Test]
    public static void ADragIsClampedAndMeasuredFromWhereItStarted()
    {
        var side = Model(+1);
        side.SetSize(300);
        side.Begin(500);
        Check.Equal(new SplitterState(340, false), side.Move(540), "the explorer (splitter at its right) grows as the pointer moves right");
        Check.Equal(new SplitterState(800, false), side.Move(5000), "kept within the room the editor keeps");
        Check.Equal(new SplitterState(250, false), side.Move(450), "back to where the pointer is, not where the grip went");
        Check.Equal(new SplitterState(250, false), side.End(), "the drag leaves the size it showed");
        Check.True(!side.Dragging, "the drag ended");

        var chat = Model(-1);
        chat.SetSize(360);
        chat.Begin(1000);
        Check.Equal(400.0, chat.Move(960).Size, "the chat column (splitter at its left) grows as the pointer moves left");
        var bottom = Model(-1, vertical: false);
        bottom.SetSize(132);
        bottom.Begin(700);
        Check.Equal(182.0, bottom.Move(650).Size, "the bottom panel grows as the pointer moves up");
        Check.True(side.Move(10) == side.State, "a move with no drag under way changes nothing");
    }

    [Test]
    public static void BelowTheSmallestSizeItSnapsClosedAndOpensAgainPastIt()
    {
        var side = Model(+1);
        side.SetSize(300);
        side.Begin(300);
        var closed = side.Move(110);                          // 300 - 190 = 110, under the 120 minimum
        Check.True(closed is { Collapsed: true, Size: 300 } && closed.Shown == 0, "under the smallest size it shows closed, its open size kept");
        Check.Equal(new SplitterState(130, false), side.Move(130), "past the smallest size it opens again at the pointer");
        side.Move(20);
        var ended = side.End();
        Check.True(ended.Collapsed && ended.Size == 300, "a drag that ends closed asks the shell to close it; it opens again at 300");
        var noSnap = Model(+1);
        noSnap.Snap = false;
        noSnap.SetSize(300);
        noSnap.Begin(300);
        Check.Equal(new SplitterState(120, false), noSnap.Move(0), "with CRAFT_SPLITTER_SNAP off the drag stops at the smallest size");
    }

    [Test]
    public static void DoubleClickKeysAndEscape()
    {
        var side = Model(+1);
        side.SetSize(500);
        Check.Equal(new SplitterState(260, false), side.Reset(), "a double-click restores the default size");
        Check.Equal(270.0, side.Key(EditorKey.Right, KeyModifiers.None)!.Value.Size, "Right widens the explorer by a step");
        Check.Equal(220.0, side.Key(EditorKey.Left, KeyModifiers.Shift)!.Value.Size, "Shift+Left narrows it by five steps");
        Check.True(side.Key(EditorKey.Up, KeyModifiers.None) is null, "Up is not a column splitter's key (it goes on to the window)");
        Check.Equal(120.0, side.Key(EditorKey.Home, KeyModifiers.None)!.Value.Size, "Home: the smallest size");
        Check.Equal(800.0, side.Key(EditorKey.End, KeyModifiers.None)!.Value.Size, "End: the largest the room allows");
        Check.Equal(260.0, side.Key(EditorKey.Enter, KeyModifiers.None)!.Value.Size, "Enter restores the default");
        var chat = Model(-1);
        chat.SetSize(360);
        Check.Equal(370.0, chat.Key(EditorKey.Left, KeyModifiers.None)!.Value.Size, "Left widens the chat column (its splitter is at its left)");
        var bottom = Model(-1, vertical: false);
        bottom.SetSize(132);
        Check.Equal(142.0, bottom.Key(EditorKey.Up, KeyModifiers.None)!.Value.Size, "Up makes the bottom panel taller");
        bottom.Begin(400);
        bottom.Move(300);
        Check.Equal(new SplitterState(142, false), bottom.Key(EditorKey.Escape, KeyModifiers.None)!.Value, "Escape during a drag puts the size back");
        Check.True(!bottom.Dragging && bottom.Key(EditorKey.Escape, KeyModifiers.None) is null, "Escape with no drag is not the splitter's");
    }

    [Test]
    public static void TheSplittersOptionsAndTheDefaultSizes()
    {
        var defaults = new CraftOptions();
        Check.True(defaults is { SplitterGrip: 10, SplitterHoverMilliseconds: 100, SplitterKeyStep: 10, SplitterSnap: true }, "the grab area 10, the highlight after 100 ms, a key step of 10, snapping on");
        var set = CraftOptions.FromVariables(name => name switch
        {
            "CRAFT_SPLITTER_GRIP" => "14",
            "CRAFT_SPLITTER_HOVER_MS" => "0",
            "CRAFT_SPLITTER_KEY_STEP" => "4",
            "CRAFT_SPLITTER_SNAP" => "false",
            _ => null,
        });
        Check.True(set is { SplitterGrip: 14, SplitterHoverMilliseconds: 0, SplitterKeyStep: 4, SplitterSnap: false }, "each is read from its variable");
        Check.Throws<ArgumentException>(() => CraftOptions.FromVariables(n => n == "CRAFT_SPLITTER_GRIP" ? "1" : null), "a grab area under 2 units is refused");
        foreach (var name in (ReadOnlySpan<string>)["CRAFT_SPLITTER_GRIP", "CRAFT_SPLITTER_HOVER_MS", "CRAFT_SPLITTER_KEY_STEP", "CRAFT_SPLITTER_SNAP"])
        {
            Check.True(CraftOptions.Variables.Any(v => v.Name == name && v.Shape.Section == SettingSections.Appearance) && CraftSettings.Allows(SettingLayer.Workspace, name),
                $"{name} is on the settings page (Appearance, Layout) and a workspace may set it");
        }

        Check.True(PanelLayout.DefaultSizeOf(ShellPanel.Explorer) == 260 && PanelLayout.DefaultSizeOf(ShellPanel.Chat) == 360 && PanelLayout.DefaultSizeOf(ShellPanel.Bottom) == 132,
            "a double-click restores each panel's option default (260, 360, 132)");
    }
}
