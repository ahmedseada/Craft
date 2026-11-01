// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Abstractions;
using Craft.Editor.Abstractions;

namespace Craft.Editor.Shell;

/// <summary>
/// The open menu of the window without a UI (phase X): a title-bar menu or a context menu (<see cref="EditorContextMenus"/>),
/// its rows (<see cref="ShellMenus.Rows(string, IEditorHost, ContextMenuTarget?)"/>, each a registered command with its icon,
/// the keys in effect, whether it can run on what the menu was opened on and why not), the row the keyboard is on, and its
/// keys: Up and Down (and Home, End) move over the rows that can run, typing jumps to the row whose title starts with what
/// was typed (X part 7: the letters typed within <see cref="TypeAheadMilliseconds"/> of each other count together; one
/// letter again goes to its next row), Enter or Space runs the row, Escape closes; a chord closes the menu and goes on to
/// the key bindings. A row with a submenu (X part 7) opens it beside itself (<see cref="Submenu"/>: Right, Enter, a click
/// or the pointer resting on it), whose rows take the keys until Left or Escape closes it. A picked row closes the menu
/// (and its submenu) and runs its command through the host, with <see cref="EditorContextMenus.Target"/> set while the
/// command starts (its <see cref="ContextMenuTarget.Argument"/> the row's), so the command acts on the row, tab or text the
/// menu was opened on. Nothing runs until a row is picked. Used on the UI thread.
/// </summary>
public sealed class MenuModel : IEditorKeyHandler
{
    private readonly EditorHost _host;
    private readonly MenuModel? _parent;
    private IReadOnlyList<MenuRow> _rows = [];
    private string _typed = "";
    private long _typedAt;

    /// <summary>The menus of <paramref name="host"/>'s commands.</summary>
    public MenuModel(EditorHost host) => _host = host ?? throw new ArgumentNullException(nameof(host));

    private MenuModel(EditorHost host, MenuModel parent)
    {
        _host = host;
        _parent = parent;
        TypeAheadMilliseconds = parent.TypeAheadMilliseconds;
        Time = parent.Time;
    }

    /// <summary>The menu open ("File", <see cref="EditorContextMenus.Tree"/>), or null.</summary>
    public string? Menu { get; private set; }

    /// <summary>What the menu was opened on (as <see cref="Describe"/> filled it in), or null (a title-bar menu, or the active document).</summary>
    public ContextMenuTarget? Target { get; private set; }

    /// <summary>The open menu's rows.</summary>
    public IReadOnlyList<MenuRow> Rows => _rows;

    /// <summary>The row the keyboard is on (-1 for none: a menu opened by the pointer starts with none).</summary>
    public int Focused { get; private set; } = -1;

    /// <summary>Whether a menu is open.</summary>
    public bool IsOpen => Menu is not null;

    /// <summary>
    /// What fills in a context menu's target before its rows are asked (X part 7: the shell's
    /// <see cref="ShellModel.DescribeMenuTarget"/> adds the language, the project kinds and whether it is the workspace's
    /// folder), or null to take targets as given.
    /// </summary>
    public Func<ContextMenuTarget, ContextMenuTarget>? Describe { get; set; }

    /// <summary>How long after a typed letter the next one still adds to it (<c>CRAFT_MENU_TYPE_AHEAD_MS</c>).</summary>
    public int TypeAheadMilliseconds { get; set; } = 700;

    /// <summary>The clock of the typing (a test moves its own).</summary>
    public TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>What was typed to jump (empty when nothing was, or it is too long ago).</summary>
    public string Typed => _typed;

    /// <summary>The open submenu (its own rows and keyboard row), or null.</summary>
    public MenuModel? Submenu { get; private set; }

    /// <summary>The row the open submenu belongs to, or -1.</summary>
    public int SubmenuRow { get; private set; } = -1;

    /// <summary>The menu this one is the submenu of, or null.</summary>
    public MenuModel? Parent => _parent;

    /// <summary>Raised when a menu opened or closed (the view draws it again).</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the keyboard's row moved (the view moves its highlight).</summary>
    public event EventHandler? FocusMoved;

    /// <summary>Raised when a submenu opened or closed (the view shows or hides its card beside the row).</summary>
    public event EventHandler? SubmenuChanged;

    /// <summary>
    /// Opens <paramref name="menu"/> on <paramref name="target"/> (filled in by <see cref="Describe"/>): its rows as they
    /// are now (each command asked whether it can run on the target); with <paramref name="focusFirst"/> (the keyboard
    /// opened it) on its first row that can run. False when it has no rows.
    /// </summary>
    public bool Open(string menu, ContextMenuTarget? target = null, bool focusFirst = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menu);
        CloseSubmenu();
        if (target is not null && Describe is { } describe)
        {
            target = describe(target);
        }

        var before = EditorContextMenus.Target;
        EditorContextMenus.Target = target;
        try
        {
            _rows = ShellMenus.Rows(menu, _host, target);
        }
        finally
        {
            EditorContextMenus.Target = before;
        }

        Menu = menu;
        Target = target;
        Focused = -1;
        _typed = "";
        if (focusFirst)
        {
            Focused = Next(-1, 1);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return _rows.Count > 0;
    }

    /// <summary>Closes the menu and its submenu (nothing runs).</summary>
    public void Close()
    {
        if (Menu is null)
        {
            return;
        }

        CloseSubmenu();
        Menu = null;
        Target = null;
        _rows = [];
        Focused = -1;
        _typed = "";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts the keyboard's row on <paramref name="index"/> (the pointer moved over it); a row that cannot run is not taken.</summary>
    public void SetFocus(int index)
    {
        if (index >= -1 && index < _rows.Count && (index < 0 || _rows[index].Enabled) && index != Focused)
        {
            Focused = index;
            FocusMoved?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Moves the keyboard's row <paramref name="step"/> rows that can run on (negative: back), wrapping; the row now, or -1 when none can run.</summary>
    public int MoveFocus(int step)
    {
        int next = Next(Focused, step);
        if (next != Focused)
        {
            Focused = next;
            FocusMoved?.Invoke(this, EventArgs.Empty);
        }

        return Focused;
    }

    /// <summary>
    /// Opens the submenu of row <paramref name="index"/> beside it (its rows asked on the same target), the keyboard on its
    /// first row when <paramref name="focusFirst"/>; false for a row without one, or one that cannot run.
    /// </summary>
    public bool OpenSubmenu(int index, bool focusFirst = false)
    {
        if (index < 0 || index >= _rows.Count || _rows[index] is not { Submenu: { } submenu, Enabled: true })
        {
            return false;
        }

        if (Submenu is { } open && SubmenuRow == index)
        {
            if (focusFirst && open.Focused < 0)
            {
                open.MoveFocus(1);
            }

            return true;
        }

        CloseSubmenu();
        var child = new MenuModel(_host, this);
        child.Open(submenu, Target, focusFirst);
        Submenu = child;
        SubmenuRow = index;
        if (Focused != index)
        {
            Focused = index;
            FocusMoved?.Invoke(this, EventArgs.Empty);
        }

        SubmenuChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Closes the open submenu (the keyboard stays on its row).</summary>
    public void CloseSubmenu()
    {
        if (Submenu is not { } child)
        {
            return;
        }

        Submenu = null;
        SubmenuRow = -1;
        child.Close();
        SubmenuChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Runs row <paramref name="index"/>: the menu (and the menu it is the submenu of) closes first, then its command runs
    /// through the host with <see cref="EditorContextMenus.Target"/> set to the menu's target (its argument the row's)
    /// while it starts. A row with a submenu opens it instead. False for a row that cannot run, or that opened its submenu.
    /// </summary>
    public async Task<bool> RunAsync(int index)
    {
        if (index < 0 || index >= _rows.Count || !_rows[index].Enabled)
        {
            return false;
        }

        var row = _rows[index];
        if (row.Submenu is not null)
        {
            OpenSubmenu(index, focusFirst: true);
            return false;
        }

        var target = Target;
        if (target is not null && row.Argument is not null)
        {
            target = target with { Argument = row.Argument };
        }
        else if (target is null && row.Argument is not null)
        {
            target = new ContextMenuTarget(Menu ?? "", null) { Argument = row.Argument };
        }

        var root = this;
        while (root._parent is { } up)
        {
            root = up;
        }

        root.Close();
        Close();
        var before = EditorContextMenus.Target;
        EditorContextMenus.Target = target;
        Task<bool> run;
        try
        {
            run = _host.ExecuteCommandAsync(row.CommandId);    // the command reads the target before its first await
        }
        finally
        {
            EditorContextMenus.Target = before;
        }

        return await run.ConfigureAwait(true);
    }

    /// <summary>The menu's keys while one is open (see the type); false with none open, or for a chord that goes on to the bindings.</summary>
    public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor)
    {
        if (!IsOpen)
        {
            return false;
        }

        bool plain = (modifiers & (KeyModifiers.Control | KeyModifiers.Alt)) == 0;
        if (!plain)
        {
            Close();
            return false;
        }

        if (Submenu is { IsOpen: true } child)
        {
            if (key is EditorKey.Left or EditorKey.Escape)
            {
                CloseSubmenu();
                return true;
            }

            return child.HandleKey(key, modifiers, editor);
        }

        switch (key)
        {
            case EditorKey.Escape:
                Close();
                return true;
            case EditorKey.Right:
                if (Focused >= 0 && _rows[Focused].Submenu is not null)
                {
                    OpenSubmenu(Focused, focusFirst: true);
                }

                return true;
            case EditorKey.Left:
                return true;
            case EditorKey.Down or EditorKey.Tab when (modifiers & KeyModifiers.Shift) == 0 || key == EditorKey.Down:
                MoveFocus(1);
                return true;
            case EditorKey.Up or EditorKey.Tab:
                MoveFocus(-1);
                return true;
            case EditorKey.Home:
                Focused = -1;
                MoveFocus(1);
                return true;
            case EditorKey.End:
                Focused = _rows.Count;
                MoveFocus(-1);
                return true;
            case EditorKey.Enter or EditorKey.Space:
                if (Focused >= 0)
                {
                    _ = RunAsync(Focused);
                }

                return true;
            case var typedKey when CharOf(typedKey) is char letter:
                TypeAhead(letter);
                return true;
            default:
                return true;                                     // the menu has the keyboard while it is open
        }
    }

    /// <summary>The window's key handler with the open menu first, then <paramref name="inner"/> (the dialogs and the bindings).</summary>
    public IEditorKeyHandler Before(IEditorKeyHandler inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return new First(this, inner);
    }

    // The letter or digit a key names (the enum's names of the letters are the letters, of the digits "D0".."D9"), or null.
    private static char? CharOf(EditorKey key)
    {
        string name = key.ToString();
        if (name.Length == 1 && char.IsAsciiLetter(name[0]))
        {
            return char.ToLowerInvariant(name[0]);
        }

        return name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]) ? name[1] : null;
    }

    // Typing jumps: the letters typed within the window of each other form a prefix, looked for from the keyboard's row
    // on; the same letter again goes to the next row it starts.
    private void TypeAhead(char letter)
    {
        long now = Time.GetTimestamp();
        bool continuing = _typed.Length > 0 && Time.GetElapsedTime(_typedAt, now).TotalMilliseconds <= TypeAheadMilliseconds;
        _typed = continuing ? _typed + letter : letter.ToString();
        _typedAt = now;
        bool repeated = _typed.AsSpan().IndexOfAnyExcept(_typed[0]) < 0;
        string prefix = repeated ? _typed[..1] : _typed;
        int from = repeated ? Focused + 1 : Math.Max(Focused, 0);
        int count = _rows.Count;
        for (int step = 0; step < count; step++)
        {
            int i = (((from + step) % count) + count) % count;
            if (_rows[i].Enabled && _rows[i].Title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                if (i != Focused)
                {
                    Focused = i;
                    FocusMoved?.Invoke(this, EventArgs.Empty);
                }

                return;
            }
        }
    }

    private int Next(int from, int step)
    {
        int count = _rows.Count;
        if (count == 0)
        {
            return -1;
        }

        int at = from;
        for (int tried = 0; tried < count; tried++)
        {
            at = at < 0 && step < 0 ? count - 1 : (((at + step) % count) + count) % count;
            if (_rows[at].Enabled)
            {
                return at;
            }
        }

        return -1;
    }

    private sealed class First(MenuModel menu, IEditorKeyHandler inner) : IEditorKeyHandler
    {
        public bool HandleKey(EditorKey key, KeyModifiers modifiers, IEditorInputSink? editor) =>
            menu.HandleKey(key, modifiers, editor) || inner.HandleKey(key, modifiers, editor);
    }
}

/// <summary>
/// The tooltips of the shell's icon-only controls (phase X): the action's name and, when a key runs it, its keys in
/// effect (<see cref="EditorHost.KeysOf"/>), so a remap on the Keyboard page shows in the tooltip at once.
/// </summary>
public static class ShellTooltips
{
    /// <summary>"Explorer (Ctrl+B)", or "Explorer" when no key runs <paramref name="commandId"/>.</summary>
    public static string For(EditorHost host, string title, string? commandId)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(title);
        return commandId is not null && host.KeysOf(commandId) is { } keys ? $"{title} ({keys})" : title;
    }
}
