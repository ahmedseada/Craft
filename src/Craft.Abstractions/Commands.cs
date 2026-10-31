// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>
/// A command, registered in <see cref="EditorCommands"/> under its <see cref="IKind.Name"/>, which is its id
/// ("sample.wordCount": the plug-in's name, a dot, the command's). The command palette lists it by <see cref="Title"/>; a
/// key binding (<see cref="EditorKeyBindings"/>) or a status item runs it by its id.
/// </summary>
public interface IEditorCommand : IKind
{
    /// <summary>What the palette shows ("Sample: Count words").</summary>
    string Title { get; }

    /// <summary>Whether it can run now in <paramref name="host"/> (true by default).</summary>
    bool CanExecute(IEditorHost host) => true;

    /// <summary>Runs it in <paramref name="host"/>, on the UI thread.</summary>
    Task ExecuteAsync(IEditorHost host, CancellationToken cancellationToken);
}

/// <summary>A command made of delegates (the usual way to write one).</summary>
public sealed class EditorCommand : IEditorCommand
{
    private readonly Func<IEditorHost, CancellationToken, Task> _execute;
    private readonly Func<IEditorHost, bool>? _canExecute;

    /// <summary>A command <paramref name="id"/> titled <paramref name="title"/> that runs <paramref name="execute"/>.</summary>
    public EditorCommand(string id, string title, Func<IEditorHost, CancellationToken, Task> execute, Func<IEditorHost, bool>? canExecute = null, string? summary = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(execute);
        Name = id;
        Title = title;
        Summary = summary ?? title;
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Summary { get; }

    /// <inheritdoc />
    public string Title { get; }

    /// <inheritdoc />
    public bool CanExecute(IEditorHost host) => _canExecute?.Invoke(host) ?? true;

    /// <inheritdoc />
    public Task ExecuteAsync(IEditorHost host, CancellationToken cancellationToken) => _execute(host, cancellationToken);
}

/// <summary>The commands, by id (ignoring case). Craft's library registers none; the app and the plug-ins do.</summary>
public static class EditorCommands
{
    private static readonly KindTable<IEditorCommand> Table = new(nameof(EditorCommands), "Command", "add it with EditorCommands.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a command runs when the user asks, once");

    /// <summary>Registers <paramref name="command"/> under its id.</summary>
    public static void Register(IEditorCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        Table.Register(command, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the command of that id; false when none was registered.</summary>
    public static bool Unregister(string id) => Table.Unregister(id);

    /// <summary>The registered ids, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered commands, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<IEditorCommand> All => Table.Kinds;

    /// <summary>The command of that id, or null.</summary>
    public static IEditorCommand? Find(ReadOnlySpan<char> id) => Table.Find(id);

    /// <summary>The command of that id.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IEditorCommand Get(ReadOnlySpan<char> id) => Table.Get(id);
}

/// <summary>A key with its modifiers ("Ctrl+Shift+W"), written and read as <see cref="ToString"/> and <see cref="Parse"/> do.</summary>
/// <param name="Key">The key.</param>
/// <param name="Modifiers">The modifiers held.</param>
public readonly record struct KeyChord(EditorKey Key, KeyModifiers Modifiers)
{
    // The keys' names by value (EditorKey's values run from 0 without gaps), and the values in the same order.
    private static readonly string[] KeyNames = Enum.GetNames<EditorKey>();
    private static readonly EditorKey[] KeyValues = Enum.GetValues<EditorKey>();
    private static readonly string[] NamesByValue = MakeNamesByValue();

    /// <summary>The longest text a chord is written as.</summary>
    public const int MaxLength = 32;

    /// <summary>"Ctrl+Shift+Alt+Key" (the modifiers held, in that order, then the key's name).</summary>
    public override string ToString()
    {
        Span<char> text = stackalloc char[MaxLength];
        return new string(text[..Format(text)]);
    }

    /// <summary>Writes it as <see cref="ToString"/> does into <paramref name="destination"/> (at least <see cref="MaxLength"/> long); returns the characters written.</summary>
    public int Format(Span<char> destination)
    {
        int at = 0;
        if ((Modifiers & KeyModifiers.Control) != 0)
        {
            "Ctrl+".CopyTo(destination);
            at += 5;
        }

        if ((Modifiers & KeyModifiers.Shift) != 0)
        {
            "Shift+".CopyTo(destination[at..]);
            at += 6;
        }

        if ((Modifiers & KeyModifiers.Alt) != 0)
        {
            "Alt+".CopyTo(destination[at..]);
            at += 4;
        }

        var name = NameOf(Key);
        name.CopyTo(destination[at..]);
        return at + name.Length;
    }

    /// <summary>
    /// Reads a chord: modifiers (Ctrl or Control, Shift, Alt) and a key's name (<see cref="EditorKey"/>'s, ignoring case;
    /// a digit names its key D0 to D9, "`" the backquote), joined by '+' ("ctrl+shift+w", "F5", "Alt+1").
    /// </summary>
    /// <exception cref="FormatException">The text names no key, or an unknown modifier or key (the message says which).</exception>
    public static KeyChord Parse(ReadOnlySpan<char> text) =>
        TryParse(text, out var chord, out var error) ? chord : throw new FormatException(error);

    /// <summary>As <see cref="Parse"/>, without throwing: false with the reason in <paramref name="error"/>.</summary>
    public static bool TryParse(ReadOnlySpan<char> text, out KeyChord chord, out string? error)
    {
        chord = default;
        var modifiers = KeyModifiers.None;
        var key = EditorKey.None;
        foreach (var range in text.Split('+'))
        {
            var part = text[range].Trim();
            if (key != EditorKey.None)
            {
                error = $"'{text}': the key ('{NameOf(key)}') must come last";
                return false;
            }

            if (part.Equals("ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= KeyModifiers.Control;
            }
            else if (part.Equals("shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= KeyModifiers.Shift;
            }
            else if (part.Equals("alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= KeyModifiers.Alt;
            }
            else if ((key = KeyOf(part)) == EditorKey.None)
            {
                error = $"'{text}': '{part}' is not a modifier (Ctrl, Shift, Alt) or a key ({string.Join(", ", KeyNames[1..])})";
                return false;
            }
        }

        if (key == EditorKey.None)
        {
            error = $"'{text}' names no key";
            return false;
        }

        chord = new KeyChord(key, modifiers);
        error = null;
        return true;
    }

    private static string[] MakeNamesByValue()
    {
        int max = 0;
        foreach (var value in KeyValues)
        {
            max = Math.Max(max, (int)value);
        }

        var names = new string[max + 1];
        for (int i = 0; i < KeyValues.Length; i++)
        {
            names[(int)KeyValues[i]] = KeyNames[i];
        }

        return names;
    }

    private static string NameOf(EditorKey key) =>
        (uint)key < (uint)NamesByValue.Length && NamesByValue[(int)key] is { } name ? name : nameof(EditorKey.None);

    private static EditorKey KeyOf(ReadOnlySpan<char> name)
    {
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            return EditorKey.D0 + (name[0] - '0');
        }

        if (name is "`")
        {
            return EditorKey.Backquote;
        }

        for (int i = 1; i < KeyNames.Length; i++)
        {
            if (name.Equals(KeyNames[i], StringComparison.OrdinalIgnoreCase))
            {
                return KeyValues[i];
            }
        }

        return EditorKey.None;
    }
}

/// <summary>
/// A key binding, registered in <see cref="EditorKeyBindings"/> under its chord's text (its <see cref="IKind.Name"/>,
/// "Ctrl+Shift+W"): the chord runs <paramref name="CommandId"/>. The editor's own keys come first: a binding runs only for
/// a chord the editor did not handle, unless <paramref name="OverridesEditor"/> says it takes the chord before the editor.
/// A user's binding (<c>CRAFT_KEY_BINDINGS</c>) always comes before both.
/// </summary>
/// <param name="Chord">The chord.</param>
/// <param name="CommandId">The command it runs; empty in a user's binding unbinds the chord (the editor's own key stays).</param>
/// <param name="OverridesEditor">Whether it runs before the editor's own handling of the chord.</param>
public sealed record KeyBinding(KeyChord Chord, string CommandId, bool OverridesEditor = false) : IKind
{
    /// <summary>The chord's text.</summary>
    public string Name { get; } = Chord.ToString();

    /// <inheritdoc />
    public string Summary => CommandId.Length == 0 ? $"{Name}: unbound" : $"{Name}: {CommandId}";

    /// <summary>
    /// Reads bindings written "Chord=command" and separated by ';' ("Ctrl+Shift+W=sample.wordCount;Ctrl+K=;F5=run"): an
    /// empty command unbinds the chord; a '!' before the command makes it take the chord before the editor
    /// ("Ctrl+D=!sample.duplicate").
    /// </summary>
    /// <exception cref="FormatException">An entry has no '=' or a chord that does not parse (the message says which).</exception>
    public static IReadOnlyList<KeyBinding> ParseList(ReadOnlySpan<char> text)
    {
        var bindings = new List<KeyBinding>();
        foreach (var range in text.Split(';'))
        {
            var entry = text[range].Trim();
            if (entry.IsEmpty)
            {
                continue;
            }

            int equals = entry.IndexOf('=');
            if (equals < 0)
            {
                throw new FormatException($"'{entry}' is not 'Chord=command'");
            }

            var chord = KeyChord.Parse(entry[..equals]);
            var command = entry[(equals + 1)..].Trim();
            bool overrides = command.StartsWith('!');
            if (overrides)
            {
                command = command[1..].Trim();
            }

            bindings.Add(new KeyBinding(chord, command.ToString(), overrides));
        }

        return bindings;
    }
}

/// <summary>
/// The plug-ins' and the app's key bindings, by chord ("Ctrl+Shift+W", ignoring case): the last registration of a chord
/// wins. The editor's own keys are not here (they are the editor's); see <see cref="KeyBinding"/> for the order.
/// </summary>
public static class EditorKeyBindings
{
    private static readonly KindTable<KeyBinding> Table = new(nameof(EditorKeyBindings), "Key binding", "add it with EditorKeyBindings.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a binding only names a command");

    /// <summary>Registers <paramref name="binding"/> under its chord.</summary>
    public static void Register(KeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Table.Register(binding, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the binding of that chord; false when none was registered.</summary>
    public static bool Unregister(KeyChord chord) => Table.Unregister(chord.ToString());

    /// <summary>The bound chords, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The bindings, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<KeyBinding> All => Table.Kinds;

    /// <summary>The binding of <paramref name="chord"/>, or null (no string is made: the chord is written into a stack buffer).</summary>
    public static KeyBinding? Find(KeyChord chord)
    {
        Span<char> name = stackalloc char[KeyChord.MaxLength];
        return Table.Find(name[..chord.Format(name)]);
    }
}
