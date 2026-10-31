// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Shell;

namespace Craft.Editor;

public sealed partial class EditorHost
{
    /// <summary>
    /// The keys that run <paramref name="commandId"/> now, as the user reads them ("Ctrl+Shift+P", "Ctrl+K Ctrl+S"): the
    /// user's own binding first, then the keymap's, then the registered one, then the editor's own key; a binding whose
    /// keys a layer above took for another command (or unbound) is skipped. Null when no keys run it. The palette, the
    /// welcome page and the menus read it, so a remap on the Keyboard page shows at once.
    /// </summary>
    public string? KeysOf(string commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        foreach (var (keys, command) in _userBindings)
        {
            if (command.Equals(commandId, StringComparison.OrdinalIgnoreCase))
            {
                return Text(keys.Chord, keys.Then);
            }
        }

        foreach (var (keys, binding) in _keyMapBindings)
        {
            if (binding.CommandId.Equals(commandId, StringComparison.OrdinalIgnoreCase) && !_userBindings.ContainsKey(keys))
            {
                return Text(keys.Chord, keys.Then);
            }
        }

        foreach (var binding in EditorKeyBindings.All)
        {
            var keys = (binding.Chord, binding.Then ?? default);
            if (binding.CommandId.Equals(commandId, StringComparison.OrdinalIgnoreCase) && !_userBindings.ContainsKey(keys) && !_keyMapBindings.ContainsKey(keys))
            {
                return Text(keys.Chord, keys.Item2);
            }
        }

        foreach (var (text, command) in StandardShortcuts.EditorKeys)
        {
            if (command.Equals(commandId, StringComparison.OrdinalIgnoreCase))
            {
                var (first, then) = KeyBinding.ParseKeys(text);
                var keys = (first, then ?? default);
                if (!_userBindings.ContainsKey(keys) && !_keyMapBindings.ContainsKey(keys))
                {
                    return Text(first, then ?? default);
                }
            }
        }

        return null;

        static string Text(KeyChord chord, KeyChord then) => then == default ? chord.DisplayText : $"{chord.DisplayText} {then.DisplayText}";
    }
}
