// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Text;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;

namespace Craft.Ai;

/// <summary>
/// What the AI plug-in's conversation panels share (the chat and the agent run): the entries, chips, status and busy
/// state of an <see cref="IConversationPanel"/>, changed on the host's UI thread only (work on another thread posts
/// through <see cref="IEditorHost.Dispatch(Action)"/>), with one change event per entry while a reply streams.
/// </summary>
public abstract class ConversationPanel : IConversationPanel
{
    private readonly List<ConversationEntry> _entries = [];
    private IReadOnlyList<ContextChip> _chips = [];
    private string? _status;
    private string? _subtitle;
    private bool _busy;

    /// <summary>A panel <paramref name="id"/> titled <paramref name="title"/> in <paramref name="host"/>.</summary>
    protected ConversationPanel(IEditorHost host, string id, string title)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        Id = id;
        Title = title;
    }

    /// <summary>The host.</summary>
    protected IEditorHost Host { get; }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public string Title { get; protected set; }

    /// <inheritdoc />
    public IReadOnlyList<PanelItem> Items => [.. _entries.Select(e => new PanelItem(e.Text, e.Detail))];

    /// <inheritdoc />
    public string? Subtitle
    {
        get => _subtitle;
        protected set
        {
            _subtitle = value;
            RaiseReset();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ConversationEntry> Entries => _entries;

    /// <inheritdoc />
    public IReadOnlyList<ContextChip> Chips => _chips;

    /// <inheritdoc />
    public string? Status
    {
        get => _status;
        protected set
        {
            if (!string.Equals(_status, value, StringComparison.Ordinal))
            {
                _status = value;
                RaiseReset();
            }
        }
    }

    /// <inheritdoc />
    public abstract string Placeholder { get; }

    /// <inheritdoc />
    public virtual string? EmptyText => null;

    /// <inheritdoc />
    public bool IsBusy
    {
        get => _busy;
        protected set
        {
            if (_busy != value)
            {
                _busy = value;
                RaiseReset();
            }
        }
    }

    /// <inheritdoc />
    public abstract IReadOnlyList<PanelAction> HeaderActions { get; }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public event EventHandler<ConversationChangedEventArgs>? ConversationChanged;

    /// <inheritdoc />
    public virtual void RemoveChip(string id) => SetChips([.. _chips.Where(c => !string.Equals(c.Id, id, StringComparison.Ordinal))]);

    /// <inheritdoc />
    public abstract Task SubmitAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Adds <paramref name="entry"/> at the end; its index.</summary>
    protected int Add(ConversationEntry entry)
    {
        _entries.Add(entry);
        ConversationChanged?.Invoke(this, new ConversationChangedEventArgs(ConversationChange.EntryAdded, _entries.Count - 1));
        return _entries.Count - 1;
    }

    /// <summary>Replaces the entry at <paramref name="index"/>.</summary>
    protected void Update(int index, ConversationEntry entry)
    {
        if ((uint)index >= (uint)_entries.Count)
        {
            return;
        }

        _entries[index] = entry;
        ConversationChanged?.Invoke(this, new ConversationChangedEventArgs(ConversationChange.EntryUpdated, index));
    }

    /// <summary>Removes every entry.</summary>
    protected void ClearEntries()
    {
        _entries.Clear();
        RaiseReset();
    }

    /// <summary>Replaces the entries from <paramref name="start"/> on with <paramref name="entries"/> (one reset).</summary>
    protected void ReplaceFrom(int start, IEnumerable<ConversationEntry> entries)
    {
        start = Math.Clamp(start, 0, _entries.Count);
        _entries.RemoveRange(start, _entries.Count - start);
        _entries.AddRange(entries);
        RaiseReset();
    }

    /// <summary>Sets the chips.</summary>
    protected void SetChips(IReadOnlyList<ContextChip> chips)
    {
        _chips = chips;
        RaiseReset();
    }

    /// <summary>Raises a reset (and the plain panel's change).</summary>
    protected void RaiseReset()
    {
        ConversationChanged?.Invoke(this, new ConversationChangedEventArgs(ConversationChange.Reset));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Runs <paramref name="action"/> on the host's UI thread.</summary>
    protected void Post(Action action) => Host.Dispatch(action);

    /// <summary>"212 tokens · 38 tok/s" for <paramref name="tokens"/> over <paramref name="seconds"/>.</summary>
    public static string Rate(int tokens, double seconds) => seconds > 0 && tokens > 0
        ? string.Create(CultureInfo.InvariantCulture, $"{tokens:N0} tokens · {tokens / seconds:F0} tok/s")
        : string.Create(CultureInfo.InvariantCulture, $"{tokens:N0} tokens");

    /// <summary>A tool result in a line, as the design's tool row shows it ("→ 1 warning"): its first line, cut.</summary>
    public static string Summarize(ToolResult result, int width = 90)
    {
        ArgumentNullException.ThrowIfNull(result);
        string text = result.Succeeded ? result.Content : "failed: " + result.Error;
        int end = text.IndexOf('\n', StringComparison.Ordinal);
        string line = (end >= 0 ? text[..end] : text).Trim();
        return line.Length <= width ? line : string.Concat(line.AsSpan(0, width - 1), "…");
    }

    /// <summary>
    /// A reply split at its fenced code blocks: plain text as <see cref="ConversationRole.Assistant"/> entries, each block
    /// as a <see cref="ConversationRole.Code"/> entry (its language from the fence) made by <paramref name="code"/>.
    /// </summary>
    public static List<ConversationEntry> Split(string reply, Func<string, string?, ConversationEntry> code)
    {
        ArgumentNullException.ThrowIfNull(reply);
        ArgumentNullException.ThrowIfNull(code);
        var entries = new List<ConversationEntry>();
        var text = new StringBuilder();
        var block = new StringBuilder();
        string? language = null;
        bool inBlock = false;
        foreach (var raw in reply.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (!inBlock)
                {
                    Flush();
                    language = line.TrimStart()[3..].Trim() is { Length: > 0 } named ? named : null;
                    block.Clear();
                    inBlock = true;
                }
                else
                {
                    entries.Add(code(block.ToString(), language));
                    inBlock = false;
                }

                continue;
            }

            (inBlock ? block : text).Append(line).Append('\n');
        }

        if (inBlock)
        {
            entries.Add(code(block.ToString(), language));   // a block the reply did not close
        }

        Flush();
        return entries;

        void Flush()
        {
            string plain = text.ToString().Trim();
            if (plain.Length > 0)
            {
                entries.Add(new ConversationEntry(ConversationRole.Assistant, plain));
            }

            text.Clear();
        }
    }
}

/// <summary>
/// An <see cref="IToolRegistry"/> that tells who listens about each call before it runs and after (the panels' tool rows),
/// and otherwise is <paramref name="inner"/>.
/// </summary>
internal sealed class ObservedTools(IToolRegistry inner, Action<ToolCall> started, Action<ToolResult> ended) : IToolRegistry
{
    public IReadOnlyList<ToolDefinition> Definitions => inner.Definitions;

    public async Task<ToolResult> InvokeAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        started(call);
        var result = await inner.InvokeAsync(call, cancellationToken).ConfigureAwait(false);
        ended(result);
        return result;
    }

    public async Task<IReadOnlyList<ToolResult>> InvokeAsync(IReadOnlyList<ToolCall> calls, CancellationToken cancellationToken = default)
    {
        var results = new ToolResult[calls.Count];
        for (int i = 0; i < calls.Count; i++)
        {
            results[i] = await InvokeAsync(calls[i], cancellationToken).ConfigureAwait(false);
        }

        return results;
    }
}
