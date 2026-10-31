// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>Which end of the status bar an item sits at.</summary>
public enum StatusAlignment
{
    /// <summary>The left end (the editor's own items: language, position).</summary>
    Left,

    /// <summary>The right end.</summary>
    Right,
}

/// <summary>
/// An item of the status bar a plug-in adds (<see cref="IEditorHost.AddStatusItem"/>): its text, tooltip, place, and a
/// command a click runs. Raise <see cref="Changed"/> (on the UI thread) when its text or tooltip changes.
/// </summary>
public interface IStatusItem
{
    /// <summary>Its id, unique in the host ("sample.wordCount").</summary>
    string Id { get; }

    /// <summary>What it shows.</summary>
    string Text { get; }

    /// <summary>What hovering shows, or null.</summary>
    string? Tooltip { get; }

    /// <summary>Its end of the bar.</summary>
    StatusAlignment Alignment { get; }

    /// <summary>Its order within its end: higher first.</summary>
    int Priority { get; }

    /// <summary>The command a click runs, or null.</summary>
    string? CommandId { get; }

    /// <summary>Raised when <see cref="Text"/> or <see cref="Tooltip"/> changed.</summary>
    event EventHandler? Changed;
}

/// <summary>A status item whose text and tooltip are set (the usual way to make one); setting a different value raises <see cref="Changed"/>.</summary>
public sealed class StatusItem(string id, string text, StatusAlignment alignment = StatusAlignment.Right, int priority = 0, string? commandId = null) : IStatusItem
{
    private string _text = text ?? "";
    private string? _tooltip;

    /// <inheritdoc />
    public string Id { get; } = !string.IsNullOrWhiteSpace(id) ? id : throw new ArgumentException("A status item needs an id.", nameof(id));

    /// <inheritdoc />
    public StatusAlignment Alignment { get; } = alignment;

    /// <inheritdoc />
    public int Priority { get; } = priority;

    /// <inheritdoc />
    public string? CommandId { get; } = commandId;

    /// <inheritdoc />
    public string Text
    {
        get => _text;
        set
        {
            if (!string.Equals(_text, value, StringComparison.Ordinal))
            {
                _text = value ?? "";
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <inheritdoc />
    public string? Tooltip
    {
        get => _tooltip;
        set
        {
            if (!string.Equals(_tooltip, value, StringComparison.Ordinal))
            {
                _tooltip = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;
}

/// <summary>One line of a panel: its text, an optional detail, and the command a click runs.</summary>
/// <param name="Text">What it shows.</param>
/// <param name="Detail">A second, quieter text (a path, a count), or null.</param>
/// <param name="CommandId">The command a click runs, or null.</param>
public sealed record PanelItem(string Text, string? Detail = null, string? CommandId = null);

/// <summary>
/// A panel a plug-in adds to the side area (<see cref="IEditorHost.AddPanel"/>): a title and a list of items, each a line
/// of text that may run a command. The host draws it; the panel describes only what it holds, so it stays UI-free.
/// Raise <see cref="Changed"/> (on the UI thread) when <see cref="Title"/> or <see cref="Items"/> changes.
/// </summary>
public interface IEditorPanel
{
    /// <summary>Its id, unique in the host ("sample.outline").</summary>
    string Id { get; }

    /// <summary>Its title.</summary>
    string Title { get; }

    /// <summary>Its lines, in order.</summary>
    IReadOnlyList<PanelItem> Items { get; }

    /// <summary>Raised when <see cref="Title"/> or <see cref="Items"/> changed.</summary>
    event EventHandler? Changed;
}

/// <summary>A panel whose items are set (the usual way to make one); <see cref="SetItems"/> raises <see cref="Changed"/>.</summary>
public sealed class ListPanel(string id, string title) : IEditorPanel
{
    private IReadOnlyList<PanelItem> _items = [];
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
    public IReadOnlyList<PanelItem> Items => _items;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Replaces the items (copied) and raises <see cref="Changed"/>.</summary>
    public void SetItems(IEnumerable<PanelItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
