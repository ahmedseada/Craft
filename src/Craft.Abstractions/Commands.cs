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
    /// Writes it as the keyboard page and the menus show it ("Ctrl+/", "Ctrl+.", "Ctrl+`", "Ctrl+1"): as
    /// <see cref="Format"/>, with the punctuation keys and the digits as their characters. <see cref="Parse"/> reads it back.
    /// </summary>
    public int FormatDisplay(Span<char> destination)
    {
        int written = Format(destination);
        if (SymbolOf(Key) is not { } symbol)
        {
            return written;
        }

        int name = NameOf(Key).Length;
        destination[written - name] = symbol;
        return written - name + 1;
    }

    /// <summary>The chord as <see cref="FormatDisplay"/> writes it.</summary>
    public string DisplayText
    {
        get
        {
            Span<char> text = stackalloc char[MaxLength];
            return new string(text[..FormatDisplay(text)]);
        }
    }

    /// <summary>
    /// Reads a chord: modifiers (Ctrl or Control, Shift, Alt) and a key's name (<see cref="EditorKey"/>'s, ignoring case;
    /// a digit names its key D0 to D9; the punctuation keys are their characters too: "`", ",", ".", "/", "-", "=" or
    /// "+", "[", "]"), joined by '+' ("ctrl+shift+w", "F5", "Alt+1", "Ctrl+/", "Ctrl++").
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
        text = text.Trim();
        bool plus = text.Length >= 2 && text.EndsWith("++", StringComparison.Ordinal);
        if (plus)
        {
            text = text[..^2];
        }
        else if (text is "+")
        {
            chord = new KeyChord(EditorKey.Plus, KeyModifiers.None);
            error = null;
            return true;
        }

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

        if (plus)
        {
            if (key != EditorKey.None)
            {
                error = $"'{text}++': the key ('{NameOf(key)}') must come last";
                return false;
            }

            key = EditorKey.Plus;
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

    private static EditorKey? KeyOfSymbol(char symbol) => symbol switch
    {
        '`' => EditorKey.Backquote,
        ',' => EditorKey.Comma,
        '.' => EditorKey.Period,
        '/' => EditorKey.Slash,
        '-' => EditorKey.Minus,
        '=' => EditorKey.Plus,
        '[' => EditorKey.BracketLeft,
        ']' => EditorKey.BracketRight,
        _ => null,
    };

    private static char? SymbolOf(EditorKey key) => key switch
    {
        EditorKey.Backquote => '`',
        EditorKey.Comma => ',',
        EditorKey.Period => '.',
        EditorKey.Slash => '/',
        EditorKey.Minus => '-',
        EditorKey.Plus => '=',
        EditorKey.BracketLeft => '[',
        EditorKey.BracketRight => ']',
        >= EditorKey.D0 and <= EditorKey.D9 => (char)('0' + (key - EditorKey.D0)),
        _ => null,
    };

    private static string NameOf(EditorKey key) =>
        (uint)key < (uint)NamesByValue.Length && NamesByValue[(int)key] is { } name ? name : nameof(EditorKey.None);

    private static EditorKey KeyOf(ReadOnlySpan<char> name)
    {
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            return EditorKey.D0 + (name[0] - '0');
        }

        if (name.Length == 1 && KeyOfSymbol(name[0]) is { } symbol)
        {
            return symbol;
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

/// <summary>Where a key binding answers.</summary>
public enum KeyContext
{
    /// <summary>Wherever the key arrives (the editor, the folder tree, a page): the shell's commands.</summary>
    Anywhere,

    /// <summary>
    /// Only while the text editor has the keyboard, and before the editor's own keys: the commands that act on the
    /// editor's text (toggle comment, move line). A terminal or another view never runs it; the key goes there.
    /// </summary>
    TextEditor,

    /// <summary>
    /// Only while a chat's or the agent's message box (the composer) has the keyboard, before the box's own keys: send,
    /// a new line, leave. Anywhere else the key goes on as if unbound.
    /// </summary>
    Composer,
}

/// <summary>
/// A key binding, registered in <see cref="EditorKeyBindings"/> under its keys' text (its <see cref="IKind.Name"/>,
/// "Ctrl+Shift+W", or "Ctrl+K Ctrl+C" for a two-key chord): the keys run <paramref name="CommandId"/>. The editor's own
/// keys come first: a binding runs only for a chord the editor did not handle, unless <paramref name="OverridesEditor"/>
/// says it takes the chord before the editor, or its <paramref name="When"/> is <see cref="KeyContext.TextEditor"/>. A
/// user's binding (<c>CRAFT_KEY_BINDINGS</c>) always comes before both, and the chosen keymap's (<see cref="EditorKeyMaps"/>)
/// before the registered ones.
/// </summary>
/// <param name="Chord">The chord (the first of two when <paramref name="Then"/> is set).</param>
/// <param name="CommandId">The command it runs; empty in a user's binding or a keymap unbinds the keys (the editor's own key stays).</param>
/// <param name="OverridesEditor">Whether it runs before the editor's own handling of the chord.</param>
/// <param name="Then">The second chord of a two-key chord ("Ctrl+K Ctrl+C"), or null.</param>
/// <param name="When">Where it answers (<see cref="KeyContext.Anywhere"/> by default).</param>
public sealed record KeyBinding(KeyChord Chord, string CommandId, bool OverridesEditor = false, KeyChord? Then = null, KeyContext When = KeyContext.Anywhere) : IKind
{
    /// <summary>The longest text a binding's keys are written as.</summary>
    public const int MaxKeysLength = (KeyChord.MaxLength * 2) + 1;

    /// <summary>The keys' text: the chord's, and the second chord's after a space.</summary>
    public string Name { get; } = Then is { } then ? string.Concat(Chord.ToString(), " ", then.ToString()) : Chord.ToString();

    /// <summary>The keys as the keyboard page shows them ("Ctrl+K Ctrl+C", "Ctrl+/").</summary>
    public string DisplayText => Then is { } then ? string.Concat(Chord.DisplayText, " ", then.DisplayText) : Chord.DisplayText;

    /// <inheritdoc />
    public string Summary => CommandId.Length == 0 ? $"{Name}: unbound" : $"{Name}: {CommandId}";

    /// <summary>Whether its keys are <paramref name="chord"/> then <paramref name="then"/> (null: a one-key binding of <paramref name="chord"/>).</summary>
    public bool HasKeys(KeyChord chord, KeyChord? then) => Chord == chord && Nullable.Equals(Then, then);

    /// <summary>
    /// Reads keys: one chord, or two separated by spaces ("Ctrl+K Ctrl+C"); each as <see cref="KeyChord.Parse"/> reads it.
    /// </summary>
    /// <exception cref="FormatException">No chord, more than two, or a chord that does not parse (the message says which).</exception>
    public static (KeyChord Chord, KeyChord? Then) ParseKeys(ReadOnlySpan<char> text) =>
        TryParseKeys(text, out var chord, out var then, out var error) ? (chord, then) : throw new FormatException(error);

    /// <summary>As <see cref="ParseKeys"/>, without throwing: false with the reason in <paramref name="error"/>.</summary>
    public static bool TryParseKeys(ReadOnlySpan<char> text, out KeyChord chord, out KeyChord? then, out string? error)
    {
        chord = default;
        then = null;
        int count = 0;
        foreach (var range in text.Trim().Split(' '))
        {
            var part = text.Trim()[range];
            if (part.IsEmpty)
            {
                continue;
            }

            if (!KeyChord.TryParse(part, out var parsed, out error))
            {
                return false;
            }

            switch (count++)
            {
                case 0:
                    chord = parsed;
                    break;
                case 1:
                    then = parsed;
                    break;
                default:
                    error = $"'{text.Trim()}': a binding takes one chord or two (\"Ctrl+K Ctrl+C\")";
                    then = null;
                    return false;
            }
        }

        if (count == 0)
        {
            error = $"'{text.Trim()}' names no key";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Reads bindings written "Keys=command" and separated by ';' ("Ctrl+Shift+W=sample.wordCount;Ctrl+K Ctrl+C=craft.comment;F5=run;Ctrl+D="):
    /// an empty command unbinds the keys; a '!' before the command makes it take the chord before the editor
    /// ("Ctrl+D=!sample.duplicate").
    /// </summary>
    /// <exception cref="FormatException">An entry has no '=' or keys that do not parse (the message says which).</exception>
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

            // The keys may hold '=' themselves ("Ctrl+==craft.zoomIn"): SplitAt skips the "=" key of a chord.
            int split = SplitAt(entry);
            var (chord, then) = ParseKeys(entry[..split]);
            var command = entry[(split + 1)..].Trim();
            bool overrides = command.StartsWith('!');
            if (overrides)
            {
                command = command[1..].Trim();
            }

            bindings.Add(new KeyBinding(chord, command.ToString(), overrides, then));
        }

        return bindings;
    }

    /// <summary>Writes <paramref name="bindings"/> as <see cref="ParseList"/> reads them, in order.</summary>
    public static string FormatList(IEnumerable<KeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var text = new System.Text.StringBuilder();
        foreach (var binding in bindings)
        {
            if (text.Length > 0)
            {
                text.Append(';');
            }

            text.Append(binding.Name).Append('=');
            if (binding.OverridesEditor)
            {
                text.Append('!');
            }

            text.Append(binding.CommandId);
        }

        return text.ToString();
    }

    // The '=' between the keys and the command: the first '=' that is not the "=" key of a chord ("Ctrl+=", "Ctrl+= x").
    private static int SplitAt(ReadOnlySpan<char> entry)
    {
        for (int i = 0; i < entry.Length; i++)
        {
            if (entry[i] != '=')
            {
                continue;
            }

            bool isKey = i > 0 && entry[i - 1] == '+' && (i + 1 == entry.Length || entry[i + 1] is '=' or ' ');
            if (!isKey)
            {
                return i;
            }
        }

        throw new FormatException($"'{entry}' is not 'Chord=command'");
    }
}

/// <summary>
/// The plug-ins' and the app's key bindings, by their keys ("Ctrl+Shift+W", "Ctrl+K Ctrl+C", ignoring case): the last
/// registration of the keys wins. The editor's own keys are not here (they are the editor's); see <see cref="KeyBinding"/>
/// for the order. The first chords of the two-key bindings are kept as a set, so a keystroke asks whether a second key
/// is awaited without a scan or a string.
/// </summary>
public static class EditorKeyBindings
{
    private static readonly KindTable<KeyBinding> Table = new(nameof(EditorKeyBindings), "Key binding", "add it with EditorKeyBindings.Register",
        comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a binding only names a command");

    private static readonly Lock Gate = new();
    private static HashSet<KeyChord> _prefixes = [];

    /// <summary>Registers <paramref name="binding"/> under its keys.</summary>
    public static void Register(KeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        lock (Gate)
        {
            Table.Register(binding, System.Reflection.Assembly.GetCallingAssembly());
            RebuildPrefixes();
        }
    }

    /// <summary>Removes the one-key binding of that chord; false when none was registered.</summary>
    public static bool Unregister(KeyChord chord) => Unregister(chord.ToString());

    /// <summary>Removes the binding of <paramref name="binding"/>'s keys (one chord or two); false when none was registered.</summary>
    public static bool Unregister(KeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return Unregister(binding.Name);
    }

    /// <summary>The bound keys, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The bindings, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<KeyBinding> All => Table.Kinds;

    /// <summary>The one-key binding of <paramref name="chord"/>, or null (no string is made: the chord is written into a stack buffer).</summary>
    public static KeyBinding? Find(KeyChord chord)
    {
        Span<char> name = stackalloc char[KeyChord.MaxLength];
        return Table.Find(name[..chord.Format(name)]);
    }

    /// <summary>The two-key binding of <paramref name="chord"/> then <paramref name="then"/>, or null (no string is made).</summary>
    public static KeyBinding? Find(KeyChord chord, KeyChord then)
    {
        Span<char> name = stackalloc char[KeyBinding.MaxKeysLength];
        int at = chord.Format(name);
        name[at++] = ' ';
        at += then.Format(name[at..]);
        return Table.Find(name[..at]);
    }

    /// <summary>Whether a registered two-key binding starts with <paramref name="chord"/>.</summary>
    public static bool IsPrefix(KeyChord chord) => Volatile.Read(ref _prefixes).Contains(chord);

    private static bool Unregister(string name)
    {
        lock (Gate)
        {
            bool removed = Table.Unregister(name);
            if (removed)
            {
                RebuildPrefixes();
            }

            return removed;
        }
    }

    private static void RebuildPrefixes()
    {
        var prefixes = new HashSet<KeyChord>();
        foreach (var binding in Table.Kinds)
        {
            if (binding.Then is not null && binding.CommandId.Length > 0)
            {
                prefixes.Add(binding.Chord);
            }
        }

        Volatile.Write(ref _prefixes, prefixes);
    }
}

/// <summary>
/// A keymap: a named set of default bindings over the registered ones, so the defaults follow the editor a developer
/// comes from ("vscode", "visualstudio") where the two differ. Its bindings come after the user's and before the
/// registered ones; an empty command in it unbinds the keys. Chosen by <c>CRAFT_KEYMAP</c>.
/// </summary>
public interface IKeyMap : IKind
{
    /// <summary>What the keyboard page shows ("Visual Studio").</summary>
    string Title { get; }

    /// <summary>Its bindings over the registered ones.</summary>
    IReadOnlyList<KeyBinding> Bindings { get; }
}

/// <summary>A keymap made of a list.</summary>
/// <param name="Name">Its name in <see cref="EditorKeyMaps"/>.</param>
/// <param name="Title">What the keyboard page shows.</param>
/// <param name="Bindings">Its bindings over the registered ones.</param>
public sealed record KeyMap(string Name, string Title, IReadOnlyList<KeyBinding> Bindings) : IKeyMap
{
    /// <inheritdoc />
    public string Summary => $"{Title}: {Bindings.Count} bindings over the defaults";
}

/// <summary>
/// The keymaps, by name (ignoring case). The library's default is <see cref="Default"/> ("vscode": the registered
/// bindings as they are, which follow VS Code's); Craft's editor registers "visualstudio", plug-ins may add more.
/// </summary>
public static class EditorKeyMaps
{
    /// <summary>The default keymap's name: the registered bindings with nothing over them.</summary>
    public const string Default = "vscode";

    private static readonly KindTable<IKeyMap> Table = MakeTable();

    /// <summary>Registers <paramref name="keyMap"/> under its name.</summary>
    public static void Register(IKeyMap keyMap)
    {
        ArgumentNullException.ThrowIfNull(keyMap);
        Table.Register(keyMap, System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the keymap of that name; false when none was registered.</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The registered keymaps, in the order of <see cref="Names"/>.</summary>
    public static IReadOnlyList<IKeyMap> All => Table.Kinds;

    /// <summary>The keymap of that name, or null.</summary>
    public static IKeyMap? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The keymap of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IKeyMap Get(ReadOnlySpan<char> name) => Table.Get(name);

    private static KindTable<IKeyMap> MakeTable()
    {
        var table = new KindTable<IKeyMap>(nameof(EditorKeyMaps), "Keymap", "add it with EditorKeyMaps.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a keymap only names commands");
        table.RegisterDefault(new KeyMap(Default, "VS Code", []));
        return table;
    }
}
