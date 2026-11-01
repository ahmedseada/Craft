// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;

namespace Craft.Abstractions;

/// <summary>
/// A panel drawn from blocks (B11): cards, text, a row of steps, a checklist, switches, buttons, meters, small charts,
/// pickers, inputs, facts and a disclosure, which the host draws with its own controls and the theme's tokens, so a
/// plug-in's view looks and behaves as the shell's (hover, focus, disabled) without a UI library of its own. The panel
/// stays UI-free: it says what it holds, and the host calls <see cref="ActAsync"/> when the user acts on a block. Its
/// <see cref="IEditorPanel.Items"/> are the same content as lines, for a host that draws only lines. Raise
/// <see cref="IEditorPanel.Changed"/> (on the UI thread) when <see cref="Blocks"/> changed.
/// </summary>
public interface IEditorViewPanel : IEditorPanel
{
    /// <summary>Its blocks, in order: each top-level block is a card or stands alone.</summary>
    IReadOnlyList<PanelBlock> Blocks { get; }

    /// <summary>
    /// The user acted on a block (on the UI thread): <paramref name="actionId"/> is the block's (a button's, a switch's, a
    /// picker's, an input's), <paramref name="value"/> what it carries ("on" or "off" for a switch, the picked option's
    /// value, the text entered, a button's <see cref="PanelButton.Value"/>). It returns at once or runs its work off the UI thread.
    /// </summary>
    Task ActAsync(string actionId, string? value, CancellationToken cancellationToken = default);
}

/// <summary>A block's or a card's colour role, from the theme's tokens.</summary>
public enum PanelTone
{
    /// <summary>The usual text and border.</summary>
    Normal,

    /// <summary>The accent (what is happening now, the step you are on).</summary>
    Accent,

    /// <summary>Done, better, kept.</summary>
    Success,

    /// <summary>Needs attention.</summary>
    Warning,

    /// <summary>Failed: the error's colour, the words saying what to change.</summary>
    Error,
}

/// <summary>How a text block reads.</summary>
public enum PanelTextStyle
{
    /// <summary>The interface's text.</summary>
    Body,

    /// <summary>Quieter, smaller: an explanation under a control.</summary>
    Secondary,

    /// <summary>The smallest: a note, a figure's unit.</summary>
    Caption,

    /// <summary>A heading inside a card.</summary>
    Heading,

    /// <summary>The code font (a path, a sample, a command).</summary>
    Code,
}

/// <summary>A button's weight.</summary>
public enum PanelButtonKind
{
    /// <summary>The one thing to do (the accent's fill).</summary>
    Primary,

    /// <summary>A bordered button.</summary>
    Secondary,

    /// <summary>No fill and no border.</summary>
    Flat,

    /// <summary>Flat, in the error's colour (delete).</summary>
    Danger,
}

/// <summary>Where a step of a <see cref="PanelSteps"/> row stands.</summary>
public enum PanelStepState
{
    /// <summary>Not reached yet.</summary>
    Next,

    /// <summary>Where you are now.</summary>
    Current,

    /// <summary>Done.</summary>
    Done,
}

/// <summary>
/// One block of an <see cref="IEditorViewPanel"/>. Every block may carry a tooltip and the variable that sets it, which
/// the host shows small under the block (as the settings page shows a setting's variable). A host skips a kind it does
/// not know.
/// </summary>
public abstract record PanelBlock
{
    private protected PanelBlock()
    {
    }

    /// <summary>What hovering shows, or null.</summary>
    public string? Tooltip { get; init; }

    /// <summary>The variable that sets what the block shows ("CRAFT_PLUGIN_LEARNING_CAPMB"), or null.</summary>
    public string? Variable { get; init; }
}

/// <summary>
/// A card: a raised group with a title, its blocks inside. A card inside a card is drawn as a titled section, never as a
/// box inside a box.
/// </summary>
/// <param name="Title">Its title, or null.</param>
/// <param name="Blocks">What it holds, in order.</param>
public sealed record PanelCard(string? Title, IReadOnlyList<PanelBlock> Blocks) : PanelBlock
{
    /// <summary>Its colour role (an error card's border and title take the error's colour).</summary>
    public PanelTone Tone { get; init; }

    /// <summary>An icon beside the title: SVG path data on a 24 by 24 box, drawn as a stroke (<see cref="RailIcons"/>' form), or null.</summary>
    public string? Icon { get; init; }

    /// <summary>A quieter line beside the title (a state, a count), or null.</summary>
    public string? Badge { get; init; }
}

/// <summary>A line or a paragraph of text.</summary>
/// <param name="Text">What it says.</param>
/// <param name="Style">How it reads.</param>
public sealed record PanelText(string Text, PanelTextStyle Style = PanelTextStyle.Body) : PanelBlock
{
    /// <summary>Its colour role.</summary>
    public PanelTone Tone { get; init; }
}

/// <summary>A step of a <see cref="PanelSteps"/> row.</summary>
/// <param name="Title">Its name ("Record").</param>
/// <param name="Icon">SVG path data on a 24 by 24 box, drawn as a stroke, or null.</param>
/// <param name="State">Where it stands.</param>
public sealed record PanelStep(string Title, string? Icon, PanelStepState State)
{
    /// <summary>A line under it ("12 episodes"), or null.</summary>
    public string? Detail { get; init; }
}

/// <summary>A row of steps joined left to right (an illustration of how a thing works and where you are in it).</summary>
/// <param name="Steps">The steps, in order.</param>
public sealed record PanelSteps(IReadOnlyList<PanelStep> Steps) : PanelBlock;

/// <summary>A button: its title, the action it runs and its weight.</summary>
/// <param name="Title">What it says.</param>
/// <param name="ActionId">The action <see cref="IEditorViewPanel.ActAsync"/> is called with.</param>
/// <param name="Kind">Its weight.</param>
public sealed record PanelButton(string Title, string ActionId, PanelButtonKind Kind = PanelButtonKind.Secondary)
{
    /// <summary>Whether it can be pressed now.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>What hovering shows, or null.</summary>
    public string? Tooltip { get; init; }

    /// <summary>The value the action is called with, or null.</summary>
    public string? Value { get; init; }

    /// <summary>
    /// An icon (SVG path data on a 24 by 24 box, drawn as a stroke): where a block draws icon buttons (a result row, a
    /// search field), the icon shows and the title becomes its tooltip and its name for screen readers (X part 6); null:
    /// the title is drawn.
    /// </summary>
    public string? Icon { get; init; }
}

/// <summary>A row of buttons (wrapping when the panel is narrow).</summary>
/// <param name="Buttons">The buttons, in order.</param>
public sealed record PanelButtons(IReadOnlyList<PanelButton> Buttons) : PanelBlock;

/// <summary>An item of a <see cref="PanelChecklist"/>: what to do, whether it is done, and the button that does it.</summary>
/// <param name="Text">What to do.</param>
/// <param name="Done">Whether it is done.</param>
/// <param name="Action">The button that does it, or null.</param>
public sealed record PanelCheck(string Text, bool Done, PanelButton? Action = null)
{
    /// <summary>A quieter line under it, or null.</summary>
    public string? Detail { get; init; }
}

/// <summary>A numbered checklist (a guided start: what to do first, each with its button).</summary>
/// <param name="Items">The items, in order.</param>
public sealed record PanelChecklist(IReadOnlyList<PanelCheck> Items) : PanelBlock;

/// <summary>A switch: its title, its state and the action toggling it runs (with "on" or "off").</summary>
/// <param name="ActionId">The action.</param>
/// <param name="Title">What it turns on.</param>
/// <param name="On">Whether it is on.</param>
public sealed record PanelSwitch(string ActionId, string Title, bool On) : PanelBlock
{
    /// <summary>What being on means, under it (a consent's words), or null.</summary>
    public string? Detail { get; init; }

    /// <summary>Whether it can be toggled now.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>A bar: how much of a whole (storage used of its limit, a training's steps), or work without a known end.</summary>
/// <param name="Title">What it measures.</param>
/// <param name="Fraction">How much, 0 to 1; below 0 for work whose end is not known.</param>
public sealed record PanelMeter(string Title, double Fraction) : PanelBlock
{
    /// <summary>The figure beside the title ("34 KB of 64 MiB"), or null.</summary>
    public string? Detail { get; init; }

    /// <summary>The bar's colour role.</summary>
    public PanelTone Tone { get; init; } = PanelTone.Accent;
}

/// <summary>A small chart: a line through values in order (a sparkline), with marked points.</summary>
/// <param name="Title">What it shows ("Loss over rounds").</param>
/// <param name="Values">The values, oldest first.</param>
public sealed record PanelChart(string Title, IReadOnlyList<double> Values) : PanelBlock
{
    /// <summary>Which points are marked (filled; the others hollow), as many as <see cref="Values"/>, or empty for none.</summary>
    public IReadOnlyList<bool> Marks { get; init; } = [];

    /// <summary>The figure beside the title (the last value, its change), or null.</summary>
    public string? Detail { get; init; }

    /// <summary>A line under it saying what the marks are, or null.</summary>
    public string? Legend { get; init; }
}

/// <summary>An option of a <see cref="PanelPicker"/>.</summary>
/// <param name="Value">What the action is called with.</param>
/// <param name="Title">What it shows.</param>
public sealed record PanelOption(string Value, string Title)
{
    /// <summary>A quieter line shown under the picker while it is the one picked, or null.</summary>
    public string? Detail { get; init; }
}

/// <summary>A picker: one of a list of options; picking runs the action with the option's value.</summary>
/// <param name="ActionId">The action.</param>
/// <param name="Title">What is picked ("Model").</param>
/// <param name="Options">The options, in order.</param>
/// <param name="Selected">The value of the option picked now.</param>
public sealed record PanelPicker(string ActionId, string Title, IReadOnlyList<PanelOption> Options, string Selected) : PanelBlock
{
    /// <summary>Whether it can be changed now.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>A text field with a button that submits it (Enter does too): the action is called with the text.</summary>
/// <param name="ActionId">The action.</param>
/// <param name="Title">What is asked ("Where to share").</param>
/// <param name="Value">The text it starts with.</param>
public sealed record PanelInput(string ActionId, string Title, string Value) : PanelBlock
{
    /// <summary>What an empty field shows, or null.</summary>
    public string? Placeholder { get; init; }

    /// <summary>The button's title ("Save").</summary>
    public string Submit { get; init; } = "Save";

    /// <summary>Whether it takes a whole number.</summary>
    public bool Numeric { get; init; }

    /// <summary>The unit after the field ("min"), or null.</summary>
    public string? Unit { get; init; }

    /// <summary>Whether the text is code (a path, an address): the code font.</summary>
    public bool Code { get; init; }
}

/// <summary>A fact: a label and its value.</summary>
/// <param name="Label">What it is ("Trained").</param>
/// <param name="Value">Its value ("2 hours ago").</param>
public sealed record PanelFact(string Label, string Value)
{
    /// <summary>Its colour role.</summary>
    public PanelTone Tone { get; init; }
}

/// <summary>Facts as two columns, labels then values.</summary>
/// <param name="Facts">The facts, in order.</param>
public sealed record PanelFacts(IReadOnlyList<PanelFact> Facts) : PanelBlock;

/// <summary>A title that opens and closes what it holds ("What is recorded?"); the host keeps whether it is open.</summary>
/// <param name="Id">Its id, unique in the panel (its open state is kept by it across redraws).</param>
/// <param name="Title">What it says.</param>
/// <param name="Blocks">What it shows when open.</param>
public sealed record PanelDisclosure(string Id, string Title, IReadOnlyList<PanelBlock> Blocks) : PanelBlock
{
    /// <summary>Whether it starts open.</summary>
    public bool Open { get; init; }
}

/// <summary>A toggle inside a <see cref="PanelQuery"/> field (match case, whole word, regular expression).</summary>
/// <param name="ActionId">The action it runs, with "on" or "off".</param>
/// <param name="Glyph">What it shows ("Aa", "ab", ".*").</param>
/// <param name="Title">What it turns on, for its tooltip ("Match case").</param>
/// <param name="On">Whether it is on.</param>
public sealed record PanelToggle(string ActionId, string Glyph, string Title, bool On)
{
    /// <summary>Its keys, for the tooltip ("Alt+C"), or null.</summary>
    public string? Keys { get; init; }
}

/// <summary>
/// A search field (X part 6): every change of its text runs the action at once with the text (the panel waits and
/// cancels as it needs); Enter runs <see cref="SubmitActionId"/>. Its toggles sit inside the field at its right and its
/// buttons after it. The host keeps the field (and the keyboard in it) while the panel redraws.
/// </summary>
/// <param name="ActionId">The action each change runs, with the text.</param>
/// <param name="Placeholder">What an empty field shows ("Search").</param>
/// <param name="Value">Its text.</param>
public sealed record PanelQuery(string ActionId, string Placeholder, string Value) : PanelBlock
{
    /// <summary>The toggles inside the field, in order.</summary>
    public IReadOnlyList<PanelToggle> Toggles { get; init; } = [];

    /// <summary>Icon buttons after the field (Replace all), in order.</summary>
    public IReadOnlyList<PanelButton> Buttons { get; init; } = [];

    /// <summary>The action Enter runs, with the text, or null.</summary>
    public string? SubmitActionId { get; init; }

    /// <summary>Whether the text is code (a glob, a path): the code font.</summary>
    public bool Code { get; init; }

    /// <summary>Why the text cannot be used (an expression that does not parse): the field's border takes the error's colour, or null.</summary>
    public string? Problem { get; init; }

    /// <summary>A number the panel raises to take the keyboard into the field (Ctrl+Shift+F); the host focuses it when it grows.</summary>
    public int Focus { get; init; }
}

/// <summary>A highlighted part of a <see cref="PanelResultRow"/>'s text (a match), and what replaces it when a replace is shown.</summary>
/// <param name="Start">Its first character in the row's text.</param>
/// <param name="Length">Its characters.</param>
public sealed record PanelSpan(int Start, int Length)
{
    /// <summary>The text that would replace it (shown struck through, then the replacement), or null.</summary>
    public string? Replacement { get; init; }
}

/// <summary>A row of a <see cref="PanelResultGroup"/>: its text with its highlights, what a click does, its buttons on hover.</summary>
/// <param name="Id">Its id, unique in the results.</param>
/// <param name="Text">What it shows (a line of code, a target's name).</param>
public sealed record PanelResultRow(string Id, string Text)
{
    /// <summary>The highlighted parts of <see cref="Text"/>, in order.</summary>
    public IReadOnlyList<PanelSpan> Highlights { get; init; } = [];

    /// <summary>A quieter text before it (a line number), or null.</summary>
    public string? Prefix { get; init; }

    /// <summary>A quieter text after it (a kind, a path), or null.</summary>
    public string? Detail { get; init; }

    /// <summary>Whether <see cref="Text"/> is code: the code font.</summary>
    public bool Code { get; init; }

    /// <summary>An icon before it (SVG path data on a 24 by 24 box, drawn as a stroke), or null.</summary>
    public string? Icon { get; init; }

    /// <summary>Its colour role (a running target in the accent, a failure in the error's).</summary>
    public PanelTone Tone { get; init; }

    /// <summary>The action a click runs, or null.</summary>
    public string? ActionId { get; init; }

    /// <summary>The value the click's action carries, or null.</summary>
    public string? Value { get; init; }

    /// <summary>Icon buttons at its right, shown while the pointer is over it or it has the keyboard.</summary>
    public IReadOnlyList<PanelButton> Buttons { get; init; } = [];

    /// <summary>A short badge at its right that always shows ("running", "default"), or null.</summary>
    public string? Badge { get; init; }

    /// <summary>Its level in a tree (X part 9: the Outline's and the hierarchies' rows): each level is indented one step; 0 by default.</summary>
    public int Depth { get; init; }

    /// <summary>Whether it is a tree node that opens (false: closed, true: open), or null for a leaf: a chevron before it.</summary>
    public bool? Expanded { get; init; }

    /// <summary>The action a click on its chevron runs (with <see cref="Id"/>), or null.</summary>
    public string? ToggleActionId { get; init; }

    /// <summary>Whether it is the row that stands for where the user is (the symbol at the caret): highlighted and scrolled into view.</summary>
    public bool Selected { get; init; }
}

/// <summary>A group of a <see cref="PanelResults"/> block: its header (a file with its icon and count) and its rows, folded or not.</summary>
/// <param name="Id">Its id, unique in the results.</param>
/// <param name="Title">Its header's text (a file's name).</param>
/// <param name="Rows">Its rows, in order.</param>
public sealed record PanelResultGroup(string Id, string Title, IReadOnlyList<PanelResultRow> Rows)
{
    /// <summary>A quieter text after the title (the file's folder), or null.</summary>
    public string? Detail { get; init; }

    /// <summary>A count at its right ("12"), or null.</summary>
    public string? Badge { get; init; }

    /// <summary>A short badge before the title in its colour (a file's: "C#"), or null.</summary>
    public string? Glyph { get; init; }

    /// <summary>The colour of <see cref="Glyph"/>, 0xAARRGGBB.</summary>
    public uint GlyphArgb { get; init; }

    /// <summary>An icon before the title (SVG path data), when there is no <see cref="Glyph"/>, or null.</summary>
    public string? Icon { get; init; }

    /// <summary>Whether its rows are folded under its header.</summary>
    public bool Collapsed { get; init; }

    /// <summary>The action a click on its header runs (with <see cref="Id"/>): folding it, or null when it does not fold.</summary>
    public string? ToggleActionId { get; init; }

    /// <summary>Icon buttons at its header's right, shown while the pointer is over it.</summary>
    public IReadOnlyList<PanelButton> Buttons { get; init; } = [];

    /// <summary>A tooltip for its header (the file's full path), or null.</summary>
    public string? Tooltip { get; init; }
}

/// <summary>
/// A list of results in groups (X part 6: a search's files and their matching lines; the Run view's projects and
/// scripts): each group a header with its icon, title, folder and count that folds, each row a line with its matches
/// highlighted that runs its action when clicked and shows its buttons on hover. It fills the rest of the panel and
/// scrolls on its own, drawing only the rows in view, so thousands of rows cost what a screenful does. The host redraws
/// its rows only when <see cref="Version"/> changes, and keeps the scroll and the rows that stayed.
/// </summary>
/// <param name="Id">Its id, unique in the panel.</param>
/// <param name="Groups">The groups, in order.</param>
public sealed record PanelResults(string Id, IReadOnlyList<PanelResultGroup> Groups) : PanelBlock
{
    /// <summary>A number the panel raises whenever the groups or rows change: what the host compares (not every row).</summary>
    public int Version { get; init; }

    /// <summary>What shows when there are no groups ("No results"), or null for nothing.</summary>
    public string? Empty { get; init; }
}

/// <summary>
/// A small icon button (X part 8: a file row's Stage, a list's Stage all, the commit box's Generate): its icon, the action
/// it runs and its tooltip; with <see cref="Menu"/> a chevron beside it opens a menu of options.
/// </summary>
/// <param name="Icon">SVG path data on a 24 by 24 box, drawn as a stroke (<see cref="RailIcons"/>' form).</param>
/// <param name="ActionId">The action a press runs.</param>
/// <param name="Tooltip">What hovering shows (its name, its keys).</param>
public sealed record PanelIconButton(string Icon, string ActionId, string Tooltip)
{
    /// <summary>The value the action is called with, or null.</summary>
    public string? Value { get; init; }

    /// <summary>Whether it can be pressed now.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Its colour role (Stop in the error's colour).</summary>
    public PanelTone Tone { get; init; }

    /// <summary>The options of its menu (the chevron beside it), or none.</summary>
    public IReadOnlyList<PanelOption> Menu { get; init; } = [];

    /// <summary>The action an option of the menu runs, with the option's value; null for <see cref="ActionId"/>.</summary>
    public string? MenuActionId { get; init; }
}

/// <summary>A file of a <see cref="PanelFileList"/>: its icon (by its name), its name and folder, a state letter in its colour, and actions on hover.</summary>
/// <param name="Path">Its full path (the click's and the actions' value unless they carry their own).</param>
/// <param name="Name">Its name ("Program.cs").</param>
/// <param name="Letter">Its state's letter ("M", "U"), or "".</param>
public sealed record PanelFileRow(string Path, string Name, string Letter)
{
    /// <summary>Its folder, quieter beside the name ("src/Core"), or null.</summary>
    public string? Folder { get; init; }

    /// <summary>The letter's and the name's colour role.</summary>
    public PanelTone Tone { get; init; }

    /// <summary>Whether the name is struck through (a deleted file).</summary>
    public bool Struck { get; init; }

    /// <summary>The action a click on the row runs (with <see cref="Path"/>), or null.</summary>
    public string? ActionId { get; init; }

    /// <summary>The buttons shown while the pointer is over the row (and for the keyboard's focus), in order.</summary>
    public IReadOnlyList<PanelIconButton> Actions { get; init; } = [];

    /// <summary>What hovering shows, or null.</summary>
    public string? Tooltip { get; init; }
}

/// <summary>A titled group of files (X part 8: Changes, Staged changes, Merge conflicts) that opens and closes, with its count and its own actions.</summary>
/// <param name="Id">Its id, unique in the panel (its open state is kept by it across redraws).</param>
/// <param name="Title">Its title.</param>
/// <param name="Rows">Its files, in order.</param>
public sealed record PanelFileList(string Id, string Title, IReadOnlyList<PanelFileRow> Rows) : PanelBlock
{
    /// <summary>The buttons on its title's row (Stage all), in order.</summary>
    public IReadOnlyList<PanelIconButton> Actions { get; init; } = [];

    /// <summary>Whether it starts open.</summary>
    public bool Open { get; init; } = true;

    /// <summary>What it shows when empty, or null to show nothing.</summary>
    public string? Empty { get; init; }
}

/// <summary>
/// A multi-line text box (X part 8: the commit message): what the user types is told to the panel as it changes
/// (<see cref="ActionId"/> with the text); <see cref="SubmitActionId"/> runs on <see cref="SubmitKeys"/>. The box keeps the
/// user's text across redraws and takes <see cref="Text"/> again only when <see cref="Version"/> changes (a draft streamed in).
/// </summary>
/// <param name="ActionId">The action told the text as the user types (nothing is redrawn for it).</param>
/// <param name="Text">The text it shows when its <see cref="Version"/> is new.</param>
public sealed record PanelMessageBox(string ActionId, string Text) : PanelBlock
{
    /// <summary>What an empty box shows ("Message (Ctrl+Enter to commit)").</summary>
    public string? Placeholder { get; init; }

    /// <summary>Raised by the panel whenever it sets <see cref="Text"/> for the box to take; the box keeps its own text while it stays.</summary>
    public int Version { get; init; }

    /// <summary>The action <see cref="SubmitKeys"/> runs with the text, or null.</summary>
    public string? SubmitActionId { get; init; }

    /// <summary>The keys that submit ("Ctrl+Enter").</summary>
    public string SubmitKeys { get; init; } = "Ctrl+Enter";

    /// <summary>The buttons inside the box, at its top right (Generate, Stop), in order.</summary>
    public IReadOnlyList<PanelIconButton> Actions { get; init; } = [];

    /// <summary>Whether text is being written into it (the box is read-only meanwhile).</summary>
    public bool Busy { get; init; }

    /// <summary>A quieter line under it (what wrote the draft, why nothing could), or null.</summary>
    public string? Status { get; init; }

    /// <summary>The status line's colour role.</summary>
    public PanelTone StatusTone { get; init; }

    /// <summary>The lines it shows at least.</summary>
    public int Lines { get; init; } = 3;
}

/// <summary>What the blocks of a view panel say as text: one line per block (a host that draws lines, tests, a change's signature).</summary>
public static class PanelBlocks
{
    /// <summary>
    /// The block as one line of text with its state (a switch's on or off, a meter's fraction, a button's enabled), the
    /// blocks it holds within it: two blocks that say the same draw the same.
    /// </summary>
    public static string Describe(PanelBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        var text = new StringBuilder();
        Describe(block, text);
        return text.ToString();
    }

    /// <summary>The blocks as the panel's lines (<see cref="IEditorPanel.Items"/>): a card's title, then each block within it.</summary>
    public static IReadOnlyList<PanelItem> AsItems(IReadOnlyList<PanelBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        var items = new List<PanelItem>();
        foreach (var block in blocks)
        {
            Add(block, items);
        }

        return items;
    }

    /// <summary>Every block within <paramref name="blocks"/>, depth first (a card's and a disclosure's blocks after it).</summary>
    public static IEnumerable<PanelBlock> All(IReadOnlyList<PanelBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        foreach (var block in blocks)
        {
            yield return block;
            var inner = block switch
            {
                PanelCard card => card.Blocks,
                PanelDisclosure disclosure => disclosure.Blocks,
                _ => null,
            };
            if (inner is not null)
            {
                foreach (var one in All(inner))
                {
                    yield return one;
                }
            }
        }
    }

    private static void Add(PanelBlock block, List<PanelItem> items)
    {
        switch (block)
        {
            case PanelCard card:
                if (card.Title is { } title)
                {
                    items.Add(new PanelItem(title, card.Badge));
                }

                foreach (var inner in card.Blocks)
                {
                    Add(inner, items);
                }

                break;
            case PanelDisclosure disclosure:
                items.Add(new PanelItem(disclosure.Title));
                foreach (var inner in disclosure.Blocks)
                {
                    Add(inner, items);
                }

                break;
            case PanelResults results:
                foreach (var group in results.Groups)
                {
                    items.Add(new PanelItem(group.Title, group.Badge));
                    if (!group.Collapsed)
                    {
                        foreach (var row in group.Rows)
                        {
                            items.Add(new PanelItem((row.Prefix is { } prefix ? prefix + "  " : "") + row.Text, row.Detail));
                        }
                    }
                }

                break;
            default:
                var line = new StringBuilder();
                Describe(block, line, variable: false);
                items.Add(new PanelItem(line.ToString(), block.Variable));
                break;
        }
    }

    private static void Describe(PanelBlock block, StringBuilder text, bool variable = true)
    {
        var c = CultureInfo.InvariantCulture;
        switch (block)
        {
            case PanelCard card:
                text.Append("[card ").Append(card.Title).Append(' ').Append(card.Tone).Append(' ').Append(card.Badge).Append(':');
                foreach (var inner in card.Blocks)
                {
                    text.Append(' ');
                    Describe(inner, text);
                }

                text.Append(']');
                break;
            case PanelText line:
                text.Append(line.Text);
                if (line.Tone != PanelTone.Normal || line.Style != PanelTextStyle.Body)
                {
                    text.Append(" (").Append(line.Style).Append(' ').Append(line.Tone).Append(')');
                }

                break;
            case PanelSteps steps:
                text.Append(string.Join(" → ", steps.Steps.Select(s => $"{s.Title} [{s.State}]{(s.Detail is { } d ? " " + d : "")}")));
                break;
            case PanelChecklist list:
                for (int i = 0; i < list.Items.Count; i++)
                {
                    var item = list.Items[i];
                    text.Append(c, $"{(i == 0 ? "" : " ")}{i + 1}. [{(item.Done ? "x" : " ")}] {item.Text}");
                    if (item.Detail is { } detail)
                    {
                        text.Append(" (").Append(detail).Append(')');
                    }

                    if (item.Action is { } action)
                    {
                        text.Append(' ');
                        Button(action, text);
                    }
                }

                break;
            case PanelButtons buttons:
                foreach (var button in buttons.Buttons)
                {
                    Button(button, text);
                    text.Append(' ');
                }

                break;
            case PanelSwitch toggle:
                text.Append(toggle.Title).Append(": ").Append(toggle.On ? "on" : "off").Append(toggle.Enabled ? "" : " (disabled)");
                if (toggle.Detail is { } why)
                {
                    text.Append(" · ").Append(why);
                }

                break;
            case PanelMeter meter:
                text.Append(meter.Title).Append(": ")
                    .Append(meter.Fraction < 0 ? "working" : string.Create(c, $"{Math.Clamp(meter.Fraction, 0, 1) * 100:0}%"))
                    .Append(meter.Detail is { } figure ? " · " + figure : "").Append(meter.Tone == PanelTone.Accent ? "" : " (" + meter.Tone + ")");
                break;
            case PanelChart chart:
                text.Append(chart.Title).Append(": ").Append(string.Join(", ", chart.Values.Select(v => v.ToString("G4", c))));
                if (chart.Marks.Count > 0)
                {
                    text.Append(" marks ").Append(string.Concat(chart.Marks.Select(m => m ? '●' : '○')));
                }

                text.Append(chart.Detail is { } last ? " · " + last : "");
                break;
            case PanelPicker picker:
                text.Append(picker.Title).Append(": ").Append(picker.Options.FirstOrDefault(o => o.Value == picker.Selected)?.Title ?? picker.Selected)
                    .Append(" of ").Append(string.Join(" | ", picker.Options.Select(o => o.Title))).Append(picker.Enabled ? "" : " (disabled)");
                break;
            case PanelInput input:
                text.Append(input.Title).Append(": ").Append(input.Value).Append(input.Unit is { } unit ? " " + unit : "").Append(" [").Append(input.Submit).Append(']');
                break;
            case PanelFacts facts:
                text.Append(string.Join(" · ", facts.Facts.Select(f => f.Label + " " + f.Value)));
                break;
            case PanelQuery query:
                text.Append('[').Append(query.Placeholder).Append(": ").Append(query.Value).Append(']');
                foreach (var toggle in query.Toggles)
                {
                    text.Append(' ').Append(toggle.Glyph).Append(toggle.On ? " on" : " off");
                }

                foreach (var button in query.Buttons)
                {
                    text.Append(' ');
                    Button(button, text);
                }

                text.Append(query.Problem is { } problem ? " (" + problem + ")" : "").Append(c, $" #{query.Focus}");
                break;
            case PanelResults results:
                // The rows themselves are not written: the version says when they changed (thousands of rows, one number).
                text.Append(c, $"[results {results.Id} v{results.Version}: {results.Groups.Count} groups]");
                break;
            case PanelFileList files:
                text.Append("[files ").Append(files.Title).Append(c, $" ({files.Rows.Count})");
                foreach (var action in files.Actions)
                {
                    text.Append(' ');
                    IconButton(action, text);
                }

                text.Append(':');
                foreach (var row in files.Rows)
                {
                    text.Append(' ').Append(row.Letter).Append(' ').Append(row.Name);
                    if (row.Folder is { Length: > 0 } folder)
                    {
                        text.Append(" · ").Append(folder);
                    }

                    if (row.Struck)
                    {
                        text.Append(" (struck)");
                    }

                    foreach (var action in row.Actions)
                    {
                        text.Append(' ');
                        IconButton(action, text);
                    }

                    text.Append(';');
                }

                if (files.Rows.Count == 0 && files.Empty is { } empty)
                {
                    text.Append(' ').Append(empty);
                }

                text.Append(']');
                break;
            case PanelMessageBox box:
                text.Append(c, $"[box v{box.Version}{(box.Busy ? " busy" : "")}] ").Append(box.Text);
                foreach (var action in box.Actions)
                {
                    text.Append(' ');
                    IconButton(action, text);
                }

                if (box.Status is { } status)
                {
                    text.Append(" · ").Append(status);
                }

                break;
            case PanelDisclosure disclosure:
                text.Append("[▸ ").Append(disclosure.Title).Append(':');
                foreach (var inner in disclosure.Blocks)
                {
                    text.Append(' ');
                    Describe(inner, text);
                }

                text.Append(']');
                break;
            default:
                text.Append(block.GetType().Name);
                break;
        }

        if (variable && block.Variable is { } name)
        {
            text.Append(" {").Append(name).Append('}');
        }
    }

    private static void IconButton(PanelIconButton button, StringBuilder text)
    {
        text.Append('(').Append(button.Tooltip).Append(button.Enabled ? "" : " disabled");
        if (button.Menu.Count > 0)
        {
            text.Append(" ▾ ").Append(string.Join(" | ", button.Menu.Select(o => o.Title)));
        }

        text.Append(')');
    }

    private static void Button(PanelButton button, StringBuilder text) =>
        text.Append('[').Append(button.Title).Append(button.Kind == PanelButtonKind.Primary ? " *" : "").Append(button.Enabled ? "" : " (disabled)").Append(']');
}

/// <summary>A view panel whose blocks are set (the usual way to make one): <see cref="SetBlocks"/> raises <see cref="Changed"/>; actions go to a delegate.</summary>
/// <param name="id">Its id, unique in the host.</param>
/// <param name="title">Its title.</param>
/// <param name="act">What an action does (its id, its value); on the UI thread.</param>
public sealed class ViewPanel(string id, string title, Func<string, string?, CancellationToken, Task> act) : IEditorViewPanel
{
    private readonly Func<string, string?, CancellationToken, Task> _act = act ?? throw new ArgumentNullException(nameof(act));
    private IReadOnlyList<PanelBlock> _blocks = [];
    private IReadOnlyList<PanelItem>? _items;
    private string _title = title ?? "";
    private string? _badge;

    /// <inheritdoc />
    public string Id { get; } = !string.IsNullOrWhiteSpace(id) ? id : throw new ArgumentException("A panel needs an id.", nameof(id));

    /// <inheritdoc />
    public string Title
    {
        get => _title;
        set
        {
            _title = value ?? "";
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PanelBlock> Blocks => _blocks;

    /// <inheritdoc />
    /// <remarks>Setting it raises <see cref="Changed"/> when it changed.</remarks>
    public string? Badge
    {
        get => _badge;
        set
        {
            if (!string.Equals(_badge, value, StringComparison.Ordinal))
            {
                _badge = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>Made from the blocks when first read after a change.</remarks>
    public IReadOnlyList<PanelItem> Items => _items ??= PanelBlocks.AsItems(_blocks);

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Replaces the blocks (copied) and raises <see cref="Changed"/>.</summary>
    public void SetBlocks(IEnumerable<PanelBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        _blocks = [.. blocks];
        _items = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public Task ActAsync(string actionId, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        return _act(actionId, value, cancellationToken);
    }
}
