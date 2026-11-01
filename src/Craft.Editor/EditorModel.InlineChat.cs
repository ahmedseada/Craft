// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;

namespace Craft.Editor;

// X part 9 item 23: the inline chat's box and ghost diff (Ctrl+I), made on first use.
public sealed partial class EditorModel
{
    private InlineChatController? _inlineChat;

    /// <summary>The inline chat (Ctrl+I): a box in the editor whose answer shows as a ghost diff in place, applied only on Accept.</summary>
    public InlineChatController InlineChat => _inlineChat ??= new InlineChatController(this);

    /// <summary>Whether the inline chat's box is open (read without making the chat).</summary>
    public bool InlineChatOpen => _inlineChat is { IsOpen: true };

    /// <summary>Opens the inline chat at the selection's lines (else the caret's line); false for a read-only text.</summary>
    public bool OpenInlineChat() => InlineChat.Open();

    /// <summary><paramref name="position"/> after <paramref name="edits"/> (in order, in the text before them); inside an edit, its end.</summary>
    internal static int MapPosition(int position, IReadOnlyList<TextEdit> edits) => MapThrough(position, edits);

    // The box first while it has the keyboard.
    private bool InlineChatKey(EditorKey key, KeyModifiers modifiers) => _inlineChat is { HasFocus: true } chat && chat.HandleKey(key, modifiers);

    private bool InlineChatText(string text) => _inlineChat is { HasFocus: true } chat && chat.Type(text);

    private bool InlineChatPress(float viewX, float viewY) => _inlineChat is { IsOpen: true } chat && chat.PointerPressed(viewX, viewY);
}
