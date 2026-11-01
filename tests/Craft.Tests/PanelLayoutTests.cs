// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Editor.Shell;
using Craft.Tests.Runner;

namespace Craft.Tests;

/// <summary>
/// The owner's Windows run of the panel views (Search, Outline, Call hierarchy): a wrapping row of buttons takes the height
/// of its rows, so nothing under it is drawn over it, and a query field's toggles and buttons go under the box when the
/// side view's measured width would clip its text.
/// </summary>
[TestGroup("PanelLayout")]
public static class PanelLayoutTests
{
    [Test]
    public static void AWrappingRowTakesTheHeightOfItsRowsAndCentresItsItems()
    {
        // Search's "Collapse all", "Refresh", "Clear" at their measured sizes, in a 236-wide side view (260 less its padding).
        (double, double)[] buttons = [(96, 32), (78, 32), (64, 34)];
        var (places, width, height) = PanelBlockLayout.Wrap(buttons, 236, 8, 6);
        Check.True(places[0] == new PanelPlace(0, 0, 96, 32) && places[1] == new PanelPlace(104, 0, 78, 32), $"two fit the first row ({string.Join(" ", places)})");
        Check.True(places[2].X == 0 && places[2].Y == 38, $"the third wraps under them, a row gap down ({places[2]})");
        Check.Equal(72.0, height, "the block is as tall as both rows: 32 + 6 + 34 (the third's own height)");
        Check.Equal(182.0, width, "its width is the widest row's");

        var (one, oneWidth, oneHeight) = PanelBlockLayout.Wrap(buttons, double.PositiveInfinity, 8, 6);
        Check.True(one[2].Y == 0 && one[0].Y == 1 && oneWidth == 254 && oneHeight == 34, "no width limit: one row, the shorter items centred on the tallest");
        var (wide, _, wideHeight) = PanelBlockLayout.Wrap([(300, 30), (40, 30)], 200, 8, 6);
        Check.True(wide[0].X == 0 && wide[1] == new PanelPlace(0, 36, 40, 30) && wideHeight == 66, "an item wider than the row stands alone on its row, the next under it");
        var (none, noneWidth, noneHeight) = PanelBlockLayout.Wrap([], 200);
        Check.True(none.Length == 0 && noneWidth == 0 && noneHeight == 0, "no items: no height");
    }

    [Test]
    public static void AQueryFieldsControlsMoveUnderTheBoxWhenTheTextWouldBeClipped()
    {
        // Search's query: three toggles (Aa, ab, .*) and two icon buttons (replace, files to include).
        double controls = (3 * 26) + (2 * 30) + 4;
        Check.True(!PanelBlockLayout.QueryOnOneRow(260, controls), "the owner's 260-wide side view: the toggles and buttons go under the box, the text keeps the row");
        Check.True(PanelBlockLayout.QueryOnOneRow(480, controls), "a wide side view: one row");
        Check.True(PanelBlockLayout.QueryOnOneRow(controls + PanelBlockLayout.QueryTextMinimum, controls), "exactly the text's minimum left: one row");
        Check.True(PanelBlockLayout.QueryOnOneRow(120, 0), "no controls: always one row");
        Check.True(PanelBlockLayout.QueryOnOneRow(0, controls), "not measured yet: one row until the width is known");
        Check.True(!PanelBlockLayout.QueryOnOneRow(260, (1 * 26) + (1 * 30) + 4, textMinimum: 220), "a larger minimum for the text moves the Outline's filter controls under it too");
    }
}
