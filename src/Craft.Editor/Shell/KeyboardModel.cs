// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Core.Settings;

namespace Craft.Editor.Shell;

/// <summary>Where a command's keys come from.</summary>
public enum ShortcutSource
{
    /// <summary>A registered binding (Craft's defaults, a plug-in's).</summary>
    Default,

    /// <summary>The chosen keymap (<c>CRAFT_KEYMAP</c>).</summary>
    KeyMap,

    /// <summary>The user's own binding (<c>CRAFT_KEY_BINDINGS</c>).</summary>
    User,

    /// <summary>A key the editor answers itself (built in; it can be given to another command, not removed).</summary>
    Editor,
}

/// <summary>Keys of a command on the keyboard page.</summary>
/// <param name="Chord">The first chord.</param>
/// <param name="Then">The second chord of a two-key chord, or null.</param>
/// <param name="Source">Where they come from.</param>
/// <param name="Conflict">What else answers these keys, or a binding they hide, or null.</param>
public sealed record ShortcutKeys(KeyChord Chord, KeyChord? Then, ShortcutSource Source, string? Conflict)
{
    /// <summary>The keys as the page shows them ("Ctrl+K Ctrl+C").</summary>
    public string Text => Then is { } then ? $"{Chord.DisplayText} {then.DisplayText}" : Chord.DisplayText;
}

/// <summary>A command on the keyboard page with its keys in effect.</summary>
/// <param name="CommandId">The command's id (shown small).</param>
/// <param name="Title">Its title.</param>
/// <param name="Keys">Its keys in effect.</param>
/// <param name="Modified">Whether the user's bindings change its keys (so Reset does something).</param>
public sealed record ShortcutRow(string CommandId, string Title, IReadOnlyList<ShortcutKeys> Keys, bool Modified)
{
    /// <summary>Whether any of its keys has a conflict.</summary>
    public bool HasConflict => Keys.Any(k => k.Conflict is not null);
}

/// <summary>
/// Records a chord by pressing it: the first chord, then (optionally) a second for a two-key chord ("Ctrl+K Ctrl+C");
/// a third press starts again. Escape alone, before any chord, cancels.
/// </summary>
public sealed class KeyRecorder
{
    /// <summary>The first chord pressed, or null.</summary>
    public KeyChord? First { get; private set; }

    /// <summary>The second chord pressed, or null.</summary>
    public KeyChord? Then { get; private set; }

    /// <summary>The keys recorded as the page shows them ("" before any).</summary>
    public string Text => First is not { } first ? "" : Then is { } then ? $"{first.DisplayText} {then.DisplayText}" : first.DisplayText;

    /// <summary>
    /// Takes a pressed chord: true when it was recorded; false for Escape with nothing recorded (the recording is
    /// cancelled) or a key alone that a shortcut cannot start with (a letter or a digit with no Ctrl or Alt).
    /// </summary>
    public bool Press(KeyChord chord)
    {
        if (chord.Key == EditorKey.None)
        {
            return false;
        }

        if (First is null)
        {
            bool plain = (chord.Modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0;
            bool function = chord.Key is EditorKey.F3 || (chord.Key >= EditorKey.F1 && chord.Key <= EditorKey.F12);
            if (chord.Key == EditorKey.Escape || (plain && !function && chord.Key is not (EditorKey.Delete or EditorKey.Insert or EditorKey.PageUp or EditorKey.PageDown or EditorKey.Home or EditorKey.End)))
            {
                return false;
            }

            First = chord;
            return true;
        }

        if (Then is null)
        {
            Then = chord;
            return true;
        }

        First = chord;
        Then = null;
        return true;
    }

    /// <summary>Forgets what was recorded.</summary>
    public void Clear()
    {
        First = null;
        Then = null;
    }
}

/// <summary>
/// The Keyboard section of the settings page without a UI (the owner's note 16): every registered command with its keys
/// in effect (the user's bindings over the keymap's over the registered ones, and the keys the editor answers itself),
/// a search by command or by keys pressed, the conflicts, a new chord recorded and given to a command, a chord removed,
/// a command's keys or all of them reset, and the keymap chosen. Changes are the user's bindings, written to
/// <c>CRAFT_KEY_BINDINGS</c> in the user's settings (this workspace's when there is no user file) and applied at once.
/// </summary>
public sealed class KeyboardModel
{
    /// <summary>The variable the user's bindings are kept in.</summary>
    public const string Variable = "CRAFT_KEY_BINDINGS";

    private readonly ShellModel _shell;

    /// <summary>The keyboard section over <paramref name="shell"/>'s commands and settings.</summary>
    public KeyboardModel(ShellModel shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
    }

    /// <summary>The recorder of the chord being pressed.</summary>
    public KeyRecorder Recorder { get; } = new();

    /// <summary>The keymap in effect.</summary>
    public string KeyMap => _shell.Options.KeyMap;

    /// <summary>The keymaps to choose from: name and title.</summary>
    public static IReadOnlyList<(string Name, string Title)> KeyMaps => [.. EditorKeyMaps.All.Select(k => (k.Name, k.Title))];

    /// <summary>The user's bindings now.</summary>
    public IReadOnlyList<KeyBinding> UserBindings => _shell.Options.KeyBindings;

    /// <summary>Why the user's bindings cannot be changed here (the environment sets them), or null.</summary>
    public string? ReadOnlyReason => _shell.Settings.SourceOf(Variable) == SettingSource.Environment
        ? $"The environment sets {Variable}, which overrides the settings files; unset it to change the keys here."
        : null;

    /// <summary>
    /// Every command with its keys, by title; filtered by <paramref name="search"/> (the title, the id or the keys' text)
    /// and, when <paramref name="chord"/> is given, to the commands those keys (one chord, or with <paramref name="then"/>) run.
    /// </summary>
    public IReadOnlyList<ShortcutRow> Rows(string? search = null, KeyChord? chord = null, KeyChord? then = null)
    {
        var effective = Effective();
        var byCommand = new Dictionary<string, List<ShortcutKeys>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (keys, (command, source, conflict)) in effective)
        {
            if (command.Length == 0)
            {
                continue;
            }

            Add(byCommand, command, new ShortcutKeys(keys.Chord, keys.Then == default ? null : keys.Then, source, conflict));
        }

        foreach (var (text, command) in StandardShortcuts.EditorKeys)
        {
            var (first, second) = KeyBinding.ParseKeys(text);
            var key = (first, second ?? default);
            if (effective.TryGetValue(key, out var taken) && taken.Command.Length > 0)
            {
                // The user's (or a binding's) command took the editor's key: shown on that command, with the conflict; or
                // the same command already shows these keys.
                continue;
            }

            Add(byCommand, command, new ShortcutKeys(first, second, ShortcutSource.Editor, null));
        }

        var userCommands = new HashSet<string>(UserBindings.Where(b => b.CommandId.Length > 0).Select(b => b.CommandId), StringComparer.OrdinalIgnoreCase);
        foreach (var binding in UserBindings.Where(b => b.CommandId.Length == 0))
        {
            if (DefaultCommandOf(binding.Chord, binding.Then) is { } unbound)
            {
                userCommands.Add(unbound);
            }
        }

        var query = (search ?? "").Trim();
        var rows = new List<ShortcutRow>();
        foreach (var command in EditorCommands.All)
        {
            var keys = byCommand.TryGetValue(command.Name, out var list) ? list : [];
            if (chord is { } wanted && !keys.Any(k => k.Chord == wanted && Nullable.Equals(k.Then, then)))
            {
                continue;
            }

            if (query.Length > 0 && !command.Title.Contains(query, StringComparison.OrdinalIgnoreCase) && !command.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !keys.Any(k => k.Text.Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            rows.Add(new ShortcutRow(command.Name, command.Title, keys, userCommands.Contains(command.Name)));
        }

        rows.Sort(static (a, b) => string.Compare(a.Title, b.Title, StringComparison.CurrentCultureIgnoreCase));
        return rows;
    }

    /// <summary>
    /// What else <paramref name="chord"/> (then <paramref name="then"/>) runs now, other than <paramref name="commandId"/>:
    /// the command's title, or null. The page says so before the keys are given.
    /// </summary>
    public string? ConflictOf(string commandId, KeyChord chord, KeyChord? then)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        var effective = Effective();
        if (effective.TryGetValue((chord, then ?? default), out var current) && current.Command.Length > 0 && !current.Command.Equals(commandId, StringComparison.OrdinalIgnoreCase))
        {
            return TitleOf(current.Command);
        }

        if (then is null)
        {
            foreach (var (keys, (command, _, _)) in effective)
            {
                if (keys.Then != default && keys.Chord == chord && command.Length > 0)
                {
                    return $"the first key of {keys.Chord.DisplayText} {keys.Then.DisplayText} ({TitleOf(command)}), so it never runs alone";
                }
            }
        }

        foreach (var (text, command) in StandardShortcuts.EditorKeys)
        {
            var (first, second) = KeyBinding.ParseKeys(text);
            if (first == chord && Nullable.Equals(second, then) && !command.Equals(commandId, StringComparison.OrdinalIgnoreCase))
            {
                return $"{TitleOf(command)} (the editor's own key, taken from it)";
            }
        }

        return null;
    }

    /// <summary>Gives <paramref name="chord"/> (then <paramref name="then"/>) to <paramref name="commandId"/> in the user's bindings; null when done, else why not.</summary>
    public string? Add(string commandId, KeyChord chord, KeyChord? then = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        var list = UserBindings.Where(b => !b.HasKeys(chord, then)).ToList();
        list.Add(new KeyBinding(chord, commandId, Then: then));
        return Write(list);
    }

    /// <summary>Gives the recorded keys (<see cref="Recorder"/>) to <paramref name="commandId"/>; null when done, else why not.</summary>
    public string? AddRecorded(string commandId)
    {
        if (Recorder.First is not { } first)
        {
            return "Press the keys first.";
        }

        var result = Add(commandId, first, Recorder.Then);
        Recorder.Clear();
        return result;
    }

    /// <summary>
    /// Takes <paramref name="chord"/> (then <paramref name="then"/>) from <paramref name="commandId"/>: the user's binding
    /// is removed, a default or the keymap's is unbound in the user's bindings; null when done, else why not (the editor's
    /// own key stays the editor's).
    /// </summary>
    public string? Remove(string commandId, KeyChord chord, KeyChord? then = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        var list = UserBindings.ToList();
        int removed = list.RemoveAll(b => b.HasKeys(chord, then) && b.CommandId.Equals(commandId, StringComparison.OrdinalIgnoreCase));
        if (DefaultCommandOf(chord, then) is { } defaultCommand && defaultCommand.Equals(commandId, StringComparison.OrdinalIgnoreCase))
        {
            list.RemoveAll(b => b.HasKeys(chord, then));
            list.Add(new KeyBinding(chord, "", Then: then));
        }
        else if (removed == 0)
        {
            return StandardShortcuts.EditorKeys.Any(k => k.CommandId.Equals(commandId, StringComparison.OrdinalIgnoreCase) && KeyBinding.ParseKeys(k.Keys) is var (f, t) && f == chord && Nullable.Equals(t, then))
                ? "That key is the editor's own: give it to another command to take it."
                : "Those keys do not run that command.";
        }

        return Write(list);
    }

    /// <summary>Takes back the user's changes to <paramref name="commandId"/>'s keys (its own bindings, and the unbindings of its defaults).</summary>
    public string? Reset(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        var list = UserBindings.Where(b => b.CommandId.Length > 0
            ? !b.CommandId.Equals(commandId, StringComparison.OrdinalIgnoreCase)
            : !(DefaultCommandOf(b.Chord, b.Then)?.Equals(commandId, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        return list.Count == UserBindings.Count ? null : Write(list);
    }

    /// <summary>Takes every user binding back: the keymap's and the registered keys apply.</summary>
    public string? ResetAll() => UserBindings.Count == 0 ? null : Write([]);

    /// <summary>Chooses the keymap <paramref name="name"/> (the default's name takes the setting off); null when done, else why not.</summary>
    public string? SetKeyMap(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (EditorKeyMaps.Find(name) is null)
        {
            return $"The keymap '{name}' is not registered (registered: {string.Join(", ", EditorKeyMaps.Names)}).";
        }

        return _shell.ApplySetting(Layer, "CRAFT_KEYMAP", string.Equals(name, EditorKeyMaps.Default, StringComparison.OrdinalIgnoreCase) ? null : name);
    }

    private SettingLayer Layer => _shell.Settings.User is not null ? SettingLayer.User : SettingLayer.Workspace;

    private string? Write(List<KeyBinding> list)
    {
        if (ReadOnlyReason is { } reason)
        {
            return reason;
        }

        return _shell.ApplySetting(Layer, Variable, list.Count == 0 ? null : KeyBinding.FormatList(list));
    }

    private static void Add(Dictionary<string, List<ShortcutKeys>> byCommand, string command, ShortcutKeys keys)
    {
        if (!byCommand.TryGetValue(command, out var list))
        {
            list = [];
            byCommand[command] = list;
        }

        list.Add(keys);
    }

    private static string TitleOf(string commandId) => EditorCommands.Find(commandId)?.Title ?? commandId;

    // The command the keys run without the user's bindings: the keymap's, else the registered one.
    private string? DefaultCommandOf(KeyChord chord, KeyChord? then)
    {
        if (EditorKeyMaps.Find(KeyMap) is { } keyMap)
        {
            foreach (var binding in keyMap.Bindings)
            {
                if (binding.HasKeys(chord, then))
                {
                    return binding.CommandId.Length > 0 ? binding.CommandId : null;
                }
            }
        }

        var registered = then is { } second ? EditorKeyBindings.Find(chord, second) : EditorKeyBindings.Find(chord);
        return registered is { CommandId.Length: > 0 } ? registered.CommandId : null;
    }

    // Every bound keys' command in effect (user > keymap > registered), with its source and a conflict note.
    private Dictionary<(KeyChord Chord, KeyChord Then), (string Command, ShortcutSource Source, string? Conflict)> Effective()
    {
        var map = new Dictionary<(KeyChord, KeyChord), (string, ShortcutSource, string?)>();
        foreach (var binding in EditorKeyBindings.All)
        {
            map[(binding.Chord, binding.Then ?? default)] = (binding.CommandId, ShortcutSource.Default, null);
        }

        if (EditorKeyMaps.Find(KeyMap) is { } keyMap)
        {
            foreach (var binding in keyMap.Bindings)
            {
                var key = (binding.Chord, binding.Then ?? default);
                string? hidden = map.TryGetValue(key, out var below) && below.Item1.Length > 0 && !below.Item1.Equals(binding.CommandId, StringComparison.OrdinalIgnoreCase)
                    ? $"over {TitleOf(below.Item1)} (the keymap's)" : null;
                map[key] = (binding.CommandId, ShortcutSource.KeyMap, hidden);
            }
        }

        foreach (var binding in UserBindings)
        {
            var key = (binding.Chord, binding.Then ?? default);
            string? hidden = map.TryGetValue(key, out var below) && below.Item1.Length > 0 && !below.Item1.Equals(binding.CommandId, StringComparison.OrdinalIgnoreCase)
                ? $"over {TitleOf(below.Item1)}" : null;
            map[key] = (binding.CommandId, ShortcutSource.User, hidden);
        }

        // A one-key binding that is also the first key of a two-key one never runs alone; an editor's key taken by another command.
        var prefixes = new HashSet<KeyChord>(map.Where(p => p.Key.Item2 != default && p.Value.Item1.Length > 0).Select(p => p.Key.Item1));
        foreach (var key in map.Keys.ToArray())
        {
            var (command, source, conflict) = map[key];
            if (command.Length == 0)
            {
                continue;
            }

            if (key.Item2 == default && prefixes.Contains(key.Item1))
            {
                conflict ??= $"also the first key of a two-key chord, so it never runs alone";
            }

            foreach (var (text, editorCommand) in StandardShortcuts.EditorKeys)
            {
                var (first, second) = KeyBinding.ParseKeys(text);
                if (first == key.Item1 && (second ?? default) == key.Item2 && !editorCommand.Equals(command, StringComparison.OrdinalIgnoreCase))
                {
                    conflict ??= $"takes the editor's {TitleOf(editorCommand)}";
                }
            }

            map[key] = (command, source, conflict);
        }

        return map;
    }
}
