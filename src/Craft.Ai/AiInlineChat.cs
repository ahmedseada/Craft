// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Craft.Abstractions;
using Idrak.Abstraction.Generation;

namespace Craft.Ai;

/// <summary>
/// "ai" in <see cref="InlineChatProviders"/> (X part 9 item 23, Ctrl+I): the chat role's model, only once it is loaded
/// (its lease as it is: no second copy, no load of its own; not loaded, <see cref="Plan"/> says so and the box's button
/// loads it at the user's press), with the role's temperature and reply limit (<c>inlineChatMaxTokens</c> when set). The
/// model gets the lines, <c>inlineChatContextLines</c> of the file around them, the problems on them and the box's
/// earlier exchanges, and writes their replacement; its fences are left out as it streams, line by line. It never edits.
/// </summary>
public sealed class AiInlineChat(Func<AiSettings> settings, AiModels models, Func<CancellationToken, Task> load) : IInlineChatProvider
{
    /// <summary>The provider's name.</summary>
    public const string ProviderName = "ai";

    /// <summary>The instructions the model is given.</summary>
    public const string SystemPrompt = "You edit code inside a code editor. You are given some lines of a file and what the user wants done with them. "
        + "Reply with the complete replacement of those lines only, in one fenced code block, keeping their indentation and the file's style; "
        + "write new code there when asked. No explanation before or after the block.";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "the AI plug-in: the chat role's loaded model rewrites the lines, shown as a ghost diff to accept";

    /// <inheritdoc />
    public InlineChatPlan Plan()
    {
        var now = settings();
        if (!now.InlineChat)
        {
            return new InlineChatPlan(null, "Inline chat is turned off (inlineChat, CRAFT_PLUGIN_AI_INLINECHAT): turn it on in Settings › Chat.");
        }

        var role = now.ChatRole;
        if (!role.Enabled)
        {
            return new InlineChatPlan(null, AiModels.Off(ModelRoleKeys.Chat));
        }

        if (role.Model.Length == 0)
        {
            return new InlineChatPlan(null, "No chat model is set: pick one at the chat's model chip, or set the model (CRAFT_PLUGIN_AI_MODEL) in Settings › Chat.");
        }

        if (!models.IsLoaded(ModelRoleKeys.Chat))
        {
            return models.IsLoading(ModelRoleKeys.Chat)
                ? new InlineChatPlan(null, $"The chat model {role.Model} is loading: send again once it is loaded (its progress is in the status bar).")
                : new InlineChatPlan(null, $"The chat model {role.Model} is not loaded (a model loads only when you ask).") { LoadAsync = load };
        }

        return new InlineChatPlan(role.Model, null);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> EditAsync(InlineChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = settings();
        if (Plan() is { Model: null } plan)
        {
            throw new InvalidOperationException(plan.Why);
        }

        var role = now.ChatRole;
        var lease = await models.ChatAsync(cancellationToken).ConfigureAwait(false);
        var chat = lease.Chat ?? throw new NotSupportedException($"The model {lease.Model} has no chat template, so it cannot answer the inline chat: pick a chat model.");
        var options = new GenerationOptions
        {
            NumPredict = now.InlineChatMaxTokens > 0 ? now.InlineChatMaxTokens : role.MaxTokens,
            NumCtx = lease.ContextLength,
            Temperature = role.Temperature,
        };
        var lines = new LineFilter();
        await foreach (var chunk in chat.StreamAsync(new ChatRequest(Messages(request, now.InlineChatContextLines), Options: options), cancellationToken).ConfigureAwait(false))
        {
            if (chunk.Delta.Content is { Length: > 0 } piece && lines.Add(piece) is { Length: > 0 } ready)
            {
                yield return ready;
            }

            if (lines.Closed)
            {
                yield break;                                  // the block ended: what follows is prose
            }
        }

        if (lines.Flush() is { Length: > 0 } rest)
        {
            yield return rest;
        }
    }

    /// <summary>
    /// The messages for <paramref name="request"/>: the instructions, then the first exchange (the lines with the file
    /// around them and their problems, and what was asked first), each earlier answer and follow-up, and the newest instruction.
    /// </summary>
    public static IReadOnlyList<ChatMessage> Messages(InlineChatRequest request, int contextLines)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = request.Snapshot;
        int start = Math.Clamp(request.Start, 0, snapshot.Length);
        int end = Math.Clamp(request.End, start, snapshot.Length);
        int firstLine = snapshot.GetLineIndex(start);
        int lastLine = Math.Max(firstLine, snapshot.GetLineIndex(end) - (end > start && end == snapshot.GetLine(snapshot.GetLineIndex(end)).Start ? 1 : 0));
        int before = Math.Max(0, firstLine - contextLines);
        int after = Math.Min(snapshot.LineCount - 1, lastLine + contextLines);
        string name = request.FilePath is { } path ? request.WorkspaceFolder is { } root ? Path.GetRelativePath(root, path).Replace('\\', '/') : Path.GetFileName(path) : "an untitled document";
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"The file {name} ({request.LanguageName}).\n");
        if (before < firstLine)
        {
            text.Append(CultureInfo.InvariantCulture, $"Lines {before + 1}-{firstLine} before the lines to change:\n```\n");
            text.Append(snapshot.GetText(snapshot.GetLine(before).Start, snapshot.GetLine(firstLine).Start - snapshot.GetLine(before).Start).TrimEnd('\n', '\r')).Append("\n```\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"The lines to change ({firstLine + 1}-{lastLine + 1}):\n```\n");
        text.Append(snapshot.GetText(start, end - start).TrimEnd('\n', '\r')).Append("\n```\n");
        if (after > lastLine)
        {
            int from = snapshot.GetLine(lastLine + 1).Start;
            text.Append(CultureInfo.InvariantCulture, $"Lines {lastLine + 2}-{after + 1} after them:\n```\n");
            text.Append(snapshot.GetText(from, snapshot.GetLine(after).EndIncludingBreak - from).TrimEnd('\n', '\r')).Append("\n```\n");
        }

        if (request.Diagnostics.Count > 0)
        {
            text.Append("Problems on these lines:\n");
            foreach (var diagnostic in request.Diagnostics)
            {
                int line = snapshot.GetLineIndex(Math.Clamp(diagnostic.Start, 0, snapshot.Length)) + 1;
                text.Append(CultureInfo.InvariantCulture, $"- line {line}: {diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Id}: {diagnostic.Message}\n");
            }
        }

        var history = request.History;
        string first = history.Count > 0 ? history[0].Instruction : request.Instruction;
        text.Append("\nWhat to do: ").Append(first);
        var messages = new List<ChatMessage> { new("system", SystemPrompt), new("user", text.ToString()) };
        for (int i = 0; i < history.Count; i++)
        {
            messages.Add(new ChatMessage("assistant", "```\n" + history[i].Answer.TrimEnd('\n') + "\n```"));
            string next = i + 1 < history.Count ? history[i + 1].Instruction : request.Instruction;
            messages.Add(new ChatMessage("user", "Change your replacement: " + next + "\nReply with the complete new replacement of the original lines, in one fenced code block."));
        }

        return messages;
    }

    /// <summary>The model's text cut into whole lines, its code fences left out (the first opens, the next one closes).</summary>
    private sealed class LineFilter
    {
        private readonly StringBuilder _partial = new();
        private readonly StringBuilder _held = new();
        private bool _opened;
        private bool _any;

        public bool Closed { get; private set; }

        public string Add(string piece)
        {
            _partial.Append(piece);
            var ready = new StringBuilder();
            string all = _partial.ToString();
            int at = 0;
            int newline;
            while (!Closed && (newline = all.IndexOf('\n', at)) >= 0)
            {
                Take(all.AsSpan(at, newline - at + 1), ready);
                at = newline + 1;
            }

            _partial.Clear().Append(all.AsSpan(at));
            return ready.ToString();
        }

        public string Flush()
        {
            var ready = new StringBuilder();
            if (!Closed && _partial.Length > 0)
            {
                Take(_partial.ToString(), ready);
            }

            _partial.Clear();
            if (!_opened && _held.Length > 0)
            {
                ready.Insert(0, _held.ToString());           // no block came: the text held back is the answer
                _held.Clear();
            }

            return ready.ToString();
        }

        private void Take(ReadOnlySpan<char> line, StringBuilder ready)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (_opened)
                {
                    Closed = true;                            // the block's end
                }
                else
                {
                    _opened = true;                           // the block's start: the prose held before it is dropped
                    _held.Clear();
                }

                return;
            }

            if (!_opened && !_any && line.Trim().Length == 0)
            {
                return;                                       // blank lines before the block
            }

            _any = true;
            if (!_opened)
            {
                _held.Append(line);                           // before any fence: prose, or an answer with no block (held to the end)
                return;
            }

            ready.Append(line);
        }
    }
}
