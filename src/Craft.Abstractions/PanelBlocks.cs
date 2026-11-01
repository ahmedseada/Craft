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
