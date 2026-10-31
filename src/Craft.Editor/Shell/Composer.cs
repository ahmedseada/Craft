// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>The composer's commands (the chat's and the agent's message box).</summary>
public static class ComposerCommandIds
{
    /// <summary>Chat: Send the message (Enter).</summary>
    public const string Send = "craft.composer.send";

    /// <summary>Chat: A new line in the message (Shift+Enter).</summary>
    public const string NewLine = "craft.composer.newLine";

    /// <summary>Chat: Leave the message box (Escape).</summary>
    public const string Leave = "craft.composer.leave";
}

/// <summary>
/// A composer (the chat's or the agent's message box) as the target of its keys: the view sends the keys it has to
/// <see cref="EditorHost.HandleKey"/> with this as the target, so the composer's bindings (<see cref="KeyContext.Composer"/>:
/// Enter sends, Shift+Enter a new line, Escape leaves; remappable on the Keyboard page) run before the box takes the key,
/// and any key no binding takes stays the box's. Its own keys are the box's: <see cref="OnKey"/> declines every one.
/// Nothing sends on focus loss: only a key bound to send, or the Send button.
/// </summary>
/// <param name="send">Sends the message.</param>
/// <param name="newLine">Puts a line break at the box's caret.</param>
/// <param name="leave">Gives the keyboard back (to the editor).</param>
public sealed class ComposerInput(Action send, Action newLine, Action leave) : IEditorInputSink
{
    /// <summary>Sends the message.</summary>
    public void Send() => send();

    /// <summary>Puts a line break at the caret.</summary>
    public void NewLine() => newLine();

    /// <summary>Gives the keyboard back.</summary>
    public void Leave() => leave();

    /// <inheritdoc />
    public bool OnKey(EditorKey key, KeyModifiers modifiers) => false;

    /// <inheritdoc />
    public void OnText(string text)
    {
    }

    /// <summary>The composer commands, each acting on the composer the key came from (<see cref="EditorHost.KeyTarget"/>).</summary>
    public static IReadOnlyList<IEditorCommand> Commands { get; } =
    [
        Make(ComposerCommandIds.Send, "Chat: Send the message", static c => c.Send()),
        Make(ComposerCommandIds.NewLine, "Chat: A new line in the message", static c => c.NewLine()),
        Make(ComposerCommandIds.Leave, "Chat: Leave the message box", static c => c.Leave()),
    ];

    private static EditorCommand Make(string id, string title, Action<ComposerInput> run) => new(id, title, (host, _) =>
    {
        if ((host as EditorHost)?.KeyTarget is ComposerInput composer)
        {
            run(composer);
        }

        return Task.CompletedTask;
    }, static host => (host as EditorHost)?.KeyTarget is ComposerInput, title);
}
